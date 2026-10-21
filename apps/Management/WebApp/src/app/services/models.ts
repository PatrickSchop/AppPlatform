// These mirror apps/Management/Api/Contracts/*.cs (System.Text.Json, camelCase).
// Keep them in step with the C# records: a renamed field fails silently at runtime.

export type TenancyMode = 'None' | 'Single' | 'Multi';

// Applications
export interface RoleSummary {
  id: string;
  name: string;
  displayName: string;
  isDeprecated: boolean;
  assignmentCount: number;
}

export interface ApplicationSummary {
  id: string;
  key: string;
  displayName: string;
  tenancy: TenancyMode;
  tenantCount: number;
  userCount: number;
  isDisabled: boolean;
}

export interface ApplicationDetail {
  id: string;
  key: string;
  displayName: string;
  tenancy: TenancyMode;
  isDisabled: boolean;
  roles: RoleSummary[];
  tenants: TenantSummary[];
}

// Tenants
export interface TenantSummary {
  id: string;
  name: string;
  isDisabled: boolean;
}

// Teams
export interface TeamSummary {
  id: string;
  name: string;
  isDefault: boolean;
}

// Users
export interface UserMembershipInfo {
  appKey: string;
  tenantName: string;
  teamName: string;
  memberId: string;
  roles: string[];
}

export interface UserSummary {
  id: string;
  displayName: string;
  email?: string | null;
  status: string;
  createdUtc: string;
}

export interface UserDetail extends UserSummary {
  memberships: UserMembershipInfo[];
}

// Members
export interface MemberSummary {
  memberId: string;
  userId: string;
  displayName: string;
  roles: string[];
}

// Invitations
export interface InvitationCreatedResponse {
  invitationId: string;
  url: string;
  expiresUtc: string;
}

export interface InvitationSummary {
  id: string;
  expiresUtc: string;
  acceptedUtc?: string | null;
  revokedUtc?: string | null;
  status: string;
}

export interface InvitationPreviewResponse {
  displayName: string;
  applications: string[];
  expiresUtc: string;
}

export interface InvitationAcceptedResponse {
  displayName: string;
}

// Admin gate (GET api/me/tenants)
export interface MyTenantResponse {
  id: string;
  name: string;
  roles: string[];
}

export interface MyTenantsResponse {
  registered: boolean;
  displayName?: string | null;
  tenants: MyTenantResponse[];
}

// Request types
export interface PatchApplicationRequest {
  displayName?: string;
  isDisabled?: boolean;
}

export interface PatchTenantRequest {
  name?: string;
  isDisabled?: boolean;
}

export interface PatchUserRequest {
  displayName?: string;
  email?: string;
  isDisabled?: boolean;
}

export interface SetRolesRequest {
  roleIds: string[];
}
