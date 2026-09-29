import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';
import { PlatformProvider, usePlatform } from './index';

const mockFetch = vi.fn(async () => ({
  ok: true,
  status: 200,
  json: async () => ({
    api: { root: 'http://api.test' },
    auth: { tenantId: 'test', clientId: 'test', scopes: [] },
  }),
}));

beforeEach(() => {
  global.fetch = mockFetch as any;
  mockFetch.mockClear();
});

describe('PlatformProvider', () => {
  it('renders fallback while loading', () => {
    function TestComponent() {
      return <div>Loaded</div>;
    }

    const { container } = render(
      <PlatformProvider fallback={<div>Loading...</div>}>
        <TestComponent />
      </PlatformProvider>
    );

    expect(container.textContent).toContain('Loading...');
  });

  it('exports usePlatform hook', () => {
    expect(usePlatform).toBeDefined();
    expect(typeof usePlatform).toBe('function');
  });
});

describe('Error handling', () => {
  it('usePlatform throws outside provider', () => {
    function BadComponent() {
      usePlatform();
      return null;
    }

    const consoleSpy = vi.spyOn(console, 'error').mockImplementation(() => {});

    expect(() => {
      render(<BadComponent />);
    }).toThrow(/PlatformProvider/);

    consoleSpy.mockRestore();
  });
});

describe('Build verification', () => {
  it('all exports exist', async () => {
    const module = await import('./index');
    expect(module.PlatformProvider).toBeDefined();
    expect(module.usePlatform).toBeDefined();
    expect(module.useConfig).toBeDefined();
    expect(module.useApi).toBeDefined();
    expect(module.useAuth).toBeDefined();
    expect(module.useBackgroundTasks).toBeDefined();
    expect(module.useApiQuery).toBeDefined();
    expect(module.TaskProgress).toBeDefined();
  });
});
