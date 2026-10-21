// Applications
export interface RoleSummary {
  id: string;
  name: string;
  isDeprecated?: boolean;
}

export interface ApplicationSummary {
  id: string;
  name: string;
  displayName: string;
  disabled?: boolean;
}

export interface ApplicationDetail extends ApplicationSummary {
  roles: RoleSummary[];
  tenancy?: 'None' | 'Single' | 'Multi';
}

// Tenants
export interface TenantSummary {
  id: string;
  name: string;
  disabled?: boolean;
}

// Teams
export interface TeamSummary {
  id: string;
  name: string;
  isDefault?: boolean;
}

// Users
export interface UserMembershipInfo {
  id: string;
  role: string;
}

export interface UserSummary {
  id: string;
  displayName: string;
  email?: string;
}

export interface UserDetail extends UserSummary {
  memberships: UserMembershipInfo[];
}

// Members
export interface MemberSummary {
  id: string;
  displayName: string;
  roles: RoleSummary[];
}

// Invitations
export interface InvitationCreatedResponse {
  id: string;
  token: string;
}

export interface InvitationSummary {
  id: string;
  createdAt: string;
  expiresAt: string;
  status?: 'open' | 'accepted' | 'expired' | 'revoked';
  acceptedAt?: string;
}

export interface InvitationPreviewResponse {
  email?: string;
  displayName?: string;
  applications?: string[];
}

export interface InvitationAcceptedResponse {
  success: boolean;
  tenants?: MyTenantResponse[];
}

// Admin gate
export interface MyTenantResponse {
  id: string;
  name: string;
  roles: string[];
}

export interface MyTenantsResponse {
  registered: boolean;
  tenants: MyTenantResponse[];
}

// Request types
export interface PatchApplicationRequest {
  displayName?: string;
  disabled?: boolean;
}

export interface PatchTenantRequest {
  name?: string;
  disabled?: boolean;
}

export interface PatchUserRequest {
  displayName?: string;
  email?: string;
  isDisabled?: boolean;
}

export interface SetRolesRequest {
  roleIds: string[];
}
