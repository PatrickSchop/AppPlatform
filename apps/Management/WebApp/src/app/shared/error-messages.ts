export interface ApiError {
  error?: string;
  code?: string;
}

const errorCodeMap: Record<string, string> = {
  last_admin: 'This would leave no administrator',
  cannot_disable_management: 'The management application cannot be disabled',
  role_not_deprecated: 'Only deprecated roles can be deleted',
  role_in_use: 'This role is still assigned to members',
  single_tenant_app: 'Single-tenant applications cannot have additional tenants',
  last_tenant: 'An application must keep at least one tenant',
  default_team: 'The Default team cannot be deleted or renamed'
};

export function getErrorMessage(error: unknown): string {
  if (!error) return 'An error occurred';

  if (typeof error === 'object') {
    const err = error as any;
    // ApiError keeps the server's JSON body in `body`: { error: '<code>' }.
    const code = err.body?.error || err.body?.code || err.error?.code || err.code;
    if (code && errorCodeMap[code]) {
      return errorCodeMap[code];
    }
    if (typeof err.body?.error === 'string') {
      return err.body.error;
    }
    if (err.error?.error) {
      return err.error.error;
    }
    if (err.message) {
      return err.message;
    }
  }

  return String(error);
}
