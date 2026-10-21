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
    const mockFetch = vi.fn(async () => new Response(JSON.stringify({ error: 'unauthorized' }), { status: 401 }));

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
  it('calls the global fetch without binding it to the client', async () => {
    const original = global.fetch;
    // Like the browser's fetch, throws when invoked with any receiver other than the global.
    global.fetch = function (this: unknown) {
      if (this !== undefined && this !== globalThis) throw new TypeError('Illegal invocation');
      return Promise.resolve(new Response('{}', { status: 200 }));
    } as any;
    try {
      const api = new ApiClient({ baseUrl: '/api' });
      await expect(api.get('x')).resolves.toEqual({});
    } finally {
      global.fetch = original;
    }
  });
  it.each([
    ['/api', 'me/tenants'],
    ['/api', '/me/tenants'],
    ['/api/', 'me/tenants'],
  ])('joins baseUrl %s and path %s with one slash', async (baseUrl, path) => {
    const mockFetch = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }));
    const api = new ApiClient({ baseUrl, fetch: mockFetch as any });
    await api.get(path);
    expect(mockFetch.mock.calls[0][0]).toBe('/api/me/tenants');
  });
  it('reports the status and body of a failed response', async () => {
    const mockFetch = vi.fn().mockResolvedValue(new Response('{"error":"boom"}', { status: 500 }));
    const api = new ApiClient({ baseUrl: '/api', fetch: mockFetch as any });
    await expect(api.get('x')).rejects.toMatchObject({ status: 500, body: { error: 'boom' } });
  });

  it('keeps a non-JSON error body as text', async () => {
    const mockFetch = vi.fn().mockResolvedValue(new Response('<html>bad gateway</html>', { status: 502 }));
    const api = new ApiClient({ baseUrl: '/api', fetch: mockFetch as any });
    await expect(api.get('x')).rejects.toMatchObject({ status: 502, body: '<html>bad gateway</html>' });
  });
});
