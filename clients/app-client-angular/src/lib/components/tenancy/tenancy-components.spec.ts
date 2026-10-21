import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, provideRouter, Router } from '@angular/router';
import { describe, expect, it, vi } from 'vitest';
import { AUTH_CLIENT, TENANT_SESSION, TENANT_SWITCHED } from '../../injection-tokens';
import { fakeTenantSession, registered, TENANT_A, TENANT_B } from '../../testing';
import { NotRegisteredComponent } from './not-registered.component';
import { safeReturnUrl } from './safe-return-url';
import { TenantPickerComponent } from './tenant-picker.component';
import { TenantSwitcherComponent } from './tenant-switcher.component';

type Session = ReturnType<typeof fakeTenantSession>['session'];

function setup(session: Session | null, extra: unknown[] = [], returnUrl: string | null = null) {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: TENANT_SESSION, useValue: session },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: { get: () => returnUrl } } } },
      ...(extra as any[]),
    ],
  });
  const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
  return { navigate };
}

const click = (el: Element) => (el as HTMLElement).click();

describe('TenantPickerComponent', () => {
  it('lists the tenants with their roles', async () => {
    const { session } = fakeTenantSession(registered(TENANT_A, TENANT_B));
    setup(session);
    await session.load();

    const fixture = TestBed.createComponent(TenantPickerComponent);
    fixture.detectChanges();

    const options = Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('.ps-tenant-option'));
    expect(options.map(o => o.querySelector('.ps-tenant-name')!.textContent)).toEqual(['Acme', 'Globex']);
    expect(options[1].querySelector('.ps-tenant-roles')!.textContent).toBe('viewer');
  });

  it('selects the chosen tenant and continues to the page the user was going to', async () => {
    const { session } = fakeTenantSession(registered(TENANT_A, TENANT_B));
    const { navigate } = setup(session, [], '/projects?x=1');
    await session.load();
    const fixture = TestBed.createComponent(TenantPickerComponent);
    fixture.detectChanges();

    click(fixture.nativeElement.querySelectorAll('.ps-tenant-option')[1]);

    expect(session.currentTenantId).toBe(TENANT_B.tenantId);
    expect(navigate).toHaveBeenCalledWith('/projects?x=1');
  });

  it('never follows a return URL that leaves the app', async () => {
    const { session } = fakeTenantSession(registered(TENANT_A, TENANT_B));
    const { navigate } = setup(session, [], '//evil.test/phish');
    await session.load();
    const fixture = TestBed.createComponent(TenantPickerComponent);
    fixture.detectChanges();

    click(fixture.nativeElement.querySelector('.ps-tenant-option'));

    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('does not strand a user who already has a tenant', async () => {
    const { session } = fakeTenantSession(registered(TENANT_A));
    const { navigate } = setup(session, [], '/projects');
    await session.load();

    TestBed.createComponent(TenantPickerComponent).detectChanges();

    expect(navigate).toHaveBeenCalledWith('/projects');
  });

  it('sends an unregistered user on to /not-registered', async () => {
    const { session } = fakeTenantSession({ registered: false, displayName: null, tenants: [] });
    const { navigate } = setup(session);
    await session.load();

    TestBed.createComponent(TenantPickerComponent).detectChanges();

    expect(navigate).toHaveBeenCalledWith('/not-registered');
  });
});

describe('safeReturnUrl', () => {
  const bs = String.fromCharCode(92);
  it.each([
    ['/projects', '/projects'],
    ['/projects?a=1#x', '/projects?a=1#x'],
    [null, '/'],
    ['', '/'],
    ['projects', '/'],
    ['//evil.test', '/'],
    ['https://evil.test', '/'],
    ['/' + bs + 'evil.test', '/'],
  ])('%s -> %s', (input, expected) => {
    expect(safeReturnUrl(input as string | null)).toBe(expected);
  });
});

describe('TenantSwitcherComponent', () => {
  it('is hidden when the user has a single tenant', async () => {
    const { session } = fakeTenantSession(registered(TENANT_A));
    setup(session);
    await session.load();

    const fixture = TestBed.createComponent(TenantSwitcherComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ps-tenant-switcher')).toBeNull();
  });

  it('is hidden when tenancy is off', () => {
    setup(null);

    const fixture = TestBed.createComponent(TenantSwitcherComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ps-tenant-switcher')).toBeNull();
  });

  it('shows the current tenant, and switching selects the other one and notifies the app', async () => {
    const { session } = fakeTenantSession(registered(TENANT_A, TENANT_B));
    const switched = vi.fn();
    setup(session, [{ provide: TENANT_SWITCHED, useValue: switched }]);
    await session.load();
    session.select(TENANT_A.tenantId);
    const fixture = TestBed.createComponent(TenantSwitcherComponent);
    fixture.detectChanges();

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('select');
    expect(Array.from(select.options).map(o => o.textContent!.trim())).toEqual(['Acme', 'Globex']);
    expect(select.value).toBe(TENANT_A.tenantId);

    select.value = TENANT_B.tenantId;
    select.dispatchEvent(new Event('change'));

    expect(session.currentTenantId).toBe(TENANT_B.tenantId);
    expect(switched).toHaveBeenCalledTimes(1);
  });

  it('does nothing when the current tenant is chosen again', async () => {
    const { session } = fakeTenantSession(registered(TENANT_A, TENANT_B));
    const switched = vi.fn();
    setup(session, [{ provide: TENANT_SWITCHED, useValue: switched }]);
    await session.load();
    session.select(TENANT_A.tenantId);
    const fixture = TestBed.createComponent(TenantSwitcherComponent);
    fixture.detectChanges();

    const select: HTMLSelectElement = fixture.nativeElement.querySelector('select');
    select.value = TENANT_A.tenantId;
    select.dispatchEvent(new Event('change'));

    expect(switched).not.toHaveBeenCalled();
  });
});

describe('NotRegisteredComponent', () => {
  it('shows who is signed in and signs out on request', () => {
    const auth = { getAccount: () => ({ username: 'q@example.test', name: 'Q Tester' }), signOut: vi.fn() };
    setup(null, [{ provide: AUTH_CLIENT, useValue: auth }]);

    const fixture = TestBed.createComponent(NotRegisteredComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Q Tester');
    expect(fixture.nativeElement.textContent).toContain('Ask an administrator');

    click(fixture.nativeElement.querySelector('button'));

    expect(auth.signOut).toHaveBeenCalled();
  });

  it('works without an auth client', () => {
    setup(null);

    const fixture = TestBed.createComponent(NotRegisteredComponent);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.ps-not-registered-account')).toBeNull();
    expect(() => click(fixture.nativeElement.querySelector('button'))).not.toThrow();
  });
});
