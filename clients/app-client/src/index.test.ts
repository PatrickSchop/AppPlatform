import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { loadConfig, resetConfigCache } from './config.js';
import { ApiClient, ApiError } from './api-client.js';
import { TaskPoller, type BackgroundTask } from './task-poller.js';

describe('loadConfig', () => {
  beforeEach(() => resetConfigCache());

  it('fetches once for two concurrent callers', async () => {
    let callCount = 0;
    const mockFetch = vi.fn(async () => {
      callCount++;
      return {
        ok: true,
        json: async () => ({ test: true }),
      };
    });

    global.fetch = mockFetch as any;

    const [result1, result2] = await Promise.all([
      loadConfig(),
      loadConfig(),
    ]);

    expect(result1).toEqual({ test: true });
    expect(result2).toEqual({ test: true });
    expect(callCount).toBe(1);
  });
});

describe('ApiClient', () => {
  it('sets the bearer header when a token is supplied', async () => {
    const mockFetch = vi.fn(async (url, init) => ({
      ok: true,
      status: 200,
      json: async () => ({ data: 'test' }),
    }));

    const client = new ApiClient({
      baseUrl: 'http://api.test',
      getAccessToken: () => Promise.resolve('my-token'),
      fetch: mockFetch as any,
    });

    await client.get('/test');

    expect(mockFetch).toHaveBeenCalled();
    const headers = mockFetch.mock.calls[0][1].headers as Headers;
    expect(headers.get('Authorization')).toBe('Bearer my-token');
  });

  it('omits the bearer header when null', async () => {
    const mockFetch = vi.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => ({ data: 'test' }),
    }));

    const client = new ApiClient({
      baseUrl: 'http://api.test',
      getAccessToken: () => Promise.resolve(null),
      fetch: mockFetch as any,
    });

    await client.get('/test');

    const headers = mockFetch.mock.calls[0][1].headers as Headers;
    expect(headers.get('Authorization')).toBeNull();
  });

  it('calls onUnauthorized and throws ApiError on 401', async () => {
    const onUnauthorized = vi.fn();
    const mockFetch = vi.fn(async () => ({
      ok: false,
      status: 401,
      json: async () => ({ error: 'unauthorized' }),
    }));

    const client = new ApiClient({
      baseUrl: 'http://api.test',
      fetch: mockFetch as any,
      onUnauthorized,
    });

    await expect(client.get('/test')).rejects.toThrow(ApiError);
    expect(onUnauthorized).toHaveBeenCalled();
  });

  it('resolves 204 to undefined', async () => {
    const mockFetch = vi.fn(async () => ({
      ok: true,
      status: 204,
    }));

    const client = new ApiClient({
      baseUrl: 'http://api.test',
      fetch: mockFetch as any,
    });

    const result = await client.post('/test');
    expect(result).toBeUndefined();
  });

  it('surfaces non-JSON error body as text', async () => {
    const mockFetch = vi.fn(async () => ({
      ok: false,
      status: 500,
      json: async () => { throw new Error('not json'); },
      text: async () => 'Internal Server Error',
    }));

    const client = new ApiClient({
      baseUrl: 'http://api.test',
      fetch: mockFetch as any,
    });

    try {
      await client.get('/test');
    } catch (error) {
      expect(error).toBeInstanceOf(ApiError);
      expect((error as ApiError).body).toBe('Internal Server Error');
    }
  });
});

describe('TaskPoller', () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('starts polling and calls listeners', async () => {
    const mockFetch = vi.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => [{ id: '1', createdDate: '2026-09-29' }] as BackgroundTask[],
    }));

    const api = new ApiClient({
      baseUrl: 'http://api.test',
      fetch: mockFetch as any,
    });

    const poller = new TaskPoller({ api, autoStart: false });
    let changeCount = 0;

    poller.subscribe(() => {
      changeCount++;
    });

    poller.start();
    await vi.runOnlyPendingTimersAsync();

    expect(mockFetch).toHaveBeenCalled();
    expect(changeCount).toBeGreaterThan(0);

    poller.stop();
  });

  it('createTask POSTs to /api/tasks', async () => {
    const mockFetch = vi.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => ({ id: 'task-123' }),
    }));

    const api = new ApiClient({
      baseUrl: 'http://api.test',
      fetch: mockFetch as any,
    });

    const poller = new TaskPoller({ api, autoStart: false });

    const taskId = await poller.createTask('TestTask', { data: 'test' });

    expect(taskId).toBe('task-123');
    expect(mockFetch).toHaveBeenCalledWith(
      'http://api.test/api/tasks',
      expect.objectContaining({ method: 'POST' })
    );

    poller.stop();
  });

  it('subscribe returns unsubscribe function', async () => {
    const mockFetch = vi.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => [] as BackgroundTask[],
    }));

    const api = new ApiClient({
      baseUrl: 'http://api.test',
      fetch: mockFetch as any,
    });

    const poller = new TaskPoller({ api, autoStart: false });
    const changes: BackgroundTask[][] = [];

    const unsubscribe = poller.subscribe((tasks) => changes.push([...tasks]));

    poller.start();
    await vi.runOnlyPendingTimersAsync();
    const countAfterFirst = changes.length;

    unsubscribe();

    // Trigger another poll by advancing time
    vi.advanceTimersByTime(30000);
    await vi.runOnlyPendingTimersAsync();

    // Changes should not grow after unsubscribe
    expect(changes.length).toBe(countAfterFirst);

    poller.stop();
  });

  it('handles errors with backoff', async () => {
    let callCount = 0;
    const mockFetch = vi.fn(async () => {
      callCount++;
      return {
        ok: false,
        status: 500,
        json: async () => ({ error: 'server error' }),
      };
    });

    const api = new ApiClient({
      baseUrl: 'http://api.test',
      fetch: mockFetch as any,
    });

    const poller = new TaskPoller({ api, autoStart: false });

    poller.start();
    await vi.runOnlyPendingTimersAsync();

    const firstError = callCount;

    // Advance time and poll again - should still happen but with backoff
    vi.advanceTimersByTime(1000);
    await vi.runOnlyPendingTimersAsync();

    expect(callCount).toBeGreaterThan(firstError);

    poller.stop();
  });

  it('exposes tasks as readonly', async () => {
    const mockFetch = vi.fn(async () => ({
      ok: true,
      status: 200,
      json: async () => [
        {
          id: '1',
          taskType: 'Test',
          status: 'Completed',
          statusMessage: 'Done',
          completionPercentage: 100,
          description: 'Test task',
          requiresNotification: false,
          createdDate: '2026-09-29',
          updatedDate: '2026-09-29',
          startedDate: null,
          completedDate: null,
        } as BackgroundTask,
      ],
    }));

    const api = new ApiClient({
      baseUrl: 'http://api.test',
      fetch: mockFetch as any,
    });

    const poller = new TaskPoller({ api, autoStart: false });
    poller.start();
    await vi.runOnlyPendingTimersAsync();

    expect(poller.tasks.length).toBe(1);
    expect(poller.tasks[0].id).toBe('1');

    poller.stop();
  });
});
