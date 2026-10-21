import { describe, it, expect, vi, afterEach } from 'vitest';
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react';
import { TenantSession, type MyTenantsResponse } from '@PS/app-client';
import { TenantProvider, TenantGate, TenantPicker, TenantSwitcher, useTenant } from './tenancy';

const A = { tenantId: 'aaaaaaaa-0000-0000-0000-000000000001', name: 'Acme', roles: ['editor'] };
const B = { tenantId: 'bbbbbbbb-0000-0000-0000-000000000002', name: 'Globex', roles: ['viewer'] };

afterEach(cleanup);

function memoryStorage() {
  const data = new Map<string, string>();
  return {
    getItem: (k: string) => data.get(k) ?? null,
    setItem: (k: string, v: string) => void data.set(k, v),
    removeItem: (k: string) => void data.delete(k),
  };
}

function makeSession(response: MyTenantsResponse | Error) {
  const get = vi.fn(async () => {
    if (response instanceof Error) throw response;
    return response;
  });
  const session = new TenantSession({
    api: { get } as any,
    header: 'X-Tenant-Id',
    storageKey: 'ps-tenant:spec',
    storage: memoryStorage(),
  });
  return { session, get };
}

const registered = (...tenants: typeof A[]): MyTenantsResponse => ({ registered: true, displayName: 'P', tenants });

async function loaded(response: MyTenantsResponse | Error) {
  const made = makeSession(response);
  await act(async () => {
    await made.session.load();
  });
  return made;
}

describe('TenantGate', () => {
  it('passes children straight through when tenancy is off', () => {
    render(
      <TenantProvider session={null}>
        <TenantGate>
          <p>app</p>
        </TenantGate>
      </TenantProvider>
    );

    expect(screen.getByText('app')).toBeTruthy();
  });

  it('shows the picker when the user has several tenants and none is chosen', async () => {
    const { session } = await loaded(registered(A, B));

    render(
      <TenantProvider session={session}>
        <TenantGate>
          <p>app</p>
        </TenantGate>
      </TenantProvider>
    );

    expect(screen.queryByText('app')).toBeNull();
    expect(screen.getByText('Acme')).toBeTruthy();
    expect(screen.getByText('Globex')).toBeTruthy();
  });

  it('shows the app after a tenant is chosen in the picker', async () => {
    const { session } = await loaded(registered(A, B));
    render(
      <TenantProvider session={session}>
        <TenantGate>
          <p>app</p>
        </TenantGate>
      </TenantProvider>
    );

    fireEvent.click(screen.getByText('Globex'));

    expect(screen.getByText('app')).toBeTruthy();
    expect(session.currentTenantId).toBe(B.tenantId);
  });

  it('shows the app straight away when the only tenant is selected automatically', async () => {
    const { session } = await loaded(registered(A));

    render(
      <TenantProvider session={session}>
        <TenantGate>
          <p>app</p>
        </TenantGate>
      </TenantProvider>
    );

    expect(screen.getByText('app')).toBeTruthy();
  });

  it('shows the not-registered content, which the app can replace', async () => {
    const { session } = await loaded({ registered: false, displayName: null, tenants: [] });

    const first = render(
      <TenantProvider session={session}>
        <TenantGate>
          <p>app</p>
        </TenantGate>
      </TenantProvider>
    );
    expect(first.container.textContent).toContain('Ask an administrator');
    first.unmount();

    render(
      <TenantProvider session={session}>
        <TenantGate notRegistered={<p>custom page</p>}>
          <p>app</p>
        </TenantGate>
      </TenantProvider>
    );
    expect(screen.getByText('custom page')).toBeTruthy();
  });

  it('shows a replaceable picker', async () => {
    const { session } = await loaded(registered(A, B));

    render(
      <TenantProvider session={session}>
        <TenantGate picker={<p>my picker</p>}>
          <p>app</p>
        </TenantGate>
      </TenantProvider>
    );

    expect(screen.getByText('my picker')).toBeTruthy();
  });

  it('loads the tenants itself when nobody has, showing the loading content meanwhile', async () => {
    const { session, get } = makeSession(registered(A, B));

    render(
      <TenantProvider session={session}>
        <TenantGate loading={<p>please wait</p>}>
          <p>app</p>
        </TenantGate>
      </TenantProvider>
    );

    expect(screen.getByText('please wait')).toBeTruthy();
    await act(async () => {
      await session.ready();
    });
    expect(get).toHaveBeenCalledTimes(1);
    expect(screen.getByText('Acme')).toBeTruthy();
  });

  it('offers a retry when the tenants could not be loaded', async () => {
    const { session, get } = await loaded(new Error('offline'));
    render(
      <TenantProvider session={session}>
        <TenantGate>
          <p>app</p>
        </TenantGate>
      </TenantProvider>
    );
    expect(screen.queryByText('app')).toBeNull();

    await act(async () => {
      fireEvent.click(screen.getByText('Try again'));
    });

    expect(get).toHaveBeenCalledTimes(2);
  });
});

describe('useTenant', () => {
  function Probe() {
    const { state, current, enabled, hasRole } = useTenant();
    return (
      <p data-testid="probe">
        {state.status}|{current?.name ?? '-'}|{String(enabled)}|{String(hasRole('viewer'))}
      </p>
    );
  }

  it('re-renders when the session changes', async () => {
    const { session } = await loaded(registered(A, B));
    render(
      <TenantProvider session={session}>
        <Probe />
      </TenantProvider>
    );
    expect(screen.getByTestId('probe').textContent).toBe('selecting|-|true|false');

    act(() => session.select(B.tenantId));

    expect(screen.getByTestId('probe').textContent).toBe('selected|Globex|true|true');

    act(() => session.reset());

    expect(screen.getByTestId('probe').textContent).toBe('selecting|-|true|false');
  });

  it('reports tenancy as disabled without a session', () => {
    render(
      <TenantProvider session={null}>
        <Probe />
      </TenantProvider>
    );

    expect(screen.getByTestId('probe').textContent).toBe('idle|-|false|false');
  });
});

describe('TenantPicker', () => {
  it('lists tenants with their roles', async () => {
    const { session } = await loaded(registered(A, B));

    render(
      <TenantProvider session={session}>
        <TenantPicker />
      </TenantProvider>
    );

    expect(screen.getByText('viewer')).toBeTruthy();
    expect(screen.getByText('editor')).toBeTruthy();
  });
});

describe('TenantSwitcher', () => {
  it('renders nothing with a single tenant', async () => {
    const { session } = await loaded(registered(A));

    const { container } = render(
      <TenantProvider session={session}>
        <TenantSwitcher />
      </TenantProvider>
    );

    expect(container.innerHTML).toBe('');
  });

  it('renders nothing when tenancy is off', () => {
    const { container } = render(
      <TenantProvider session={null}>
        <TenantSwitcher />
      </TenantProvider>
    );

    expect(container.innerHTML).toBe('');
  });

  it('shows the current tenant; choosing another selects it and notifies the app', async () => {
    const { session } = await loaded(registered(A, B));
    session.select(A.tenantId);
    const onSwitched = vi.fn();
    render(
      <TenantProvider session={session}>
        <TenantSwitcher onSwitched={onSwitched} />
      </TenantProvider>
    );
    const select = screen.getByRole('combobox') as HTMLSelectElement;
    expect(select.value).toBe(A.tenantId);

    fireEvent.change(select, { target: { value: B.tenantId } });

    expect(session.currentTenantId).toBe(B.tenantId);
    expect(onSwitched).toHaveBeenCalledTimes(1);
  });

  it('does nothing when the current tenant is chosen again', async () => {
    const { session } = await loaded(registered(A, B));
    session.select(A.tenantId);
    const onSwitched = vi.fn();
    render(
      <TenantProvider session={session}>
        <TenantSwitcher onSwitched={onSwitched} />
      </TenantProvider>
    );

    fireEvent.change(screen.getByRole('combobox'), { target: { value: A.tenantId } });

    expect(onSwitched).not.toHaveBeenCalled();
  });
});
