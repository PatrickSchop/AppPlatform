import { Injectable, inject } from '@angular/core';
import { API_CLIENT } from '@patrickschop/app-client-angular';
import type { ApiClient } from '@patrickschop/app-client';
import {
  ApplicationSummary, ApplicationDetail, TenantSummary, TeamSummary,
  UserSummary, UserDetail, MemberSummary, InvitationCreatedResponse,
  InvitationSummary, InvitationPreviewResponse, InvitationAcceptedResponse,
  PatchApplicationRequest, PatchTenantRequest, PatchUserRequest,
  CreateUserRequest, CreateUserResponse
} from './models';

@Injectable({ providedIn: 'root' })
export class AdminService {
  private api = inject(API_CLIENT) as unknown as ApiClient;

  // Applications
  getApplications(): Promise<ApplicationSummary[]> {
    return this.api.get('admin/applications');
  }

  getApplication(appId: string): Promise<ApplicationDetail> {
    return this.api.get(`admin/applications/${appId}`);
  }

  patchApplication(appId: string, body: PatchApplicationRequest): Promise<void> {
    return this.api.patch(`admin/applications/${appId}`, body);
  }

  deleteRole(appId: string, roleId: string): Promise<void> {
    return this.api.delete(`admin/applications/${appId}/roles/${roleId}`);
  }

  // Tenants
  getTenants(appId: string): Promise<TenantSummary[]> {
    return this.api.get(`admin/applications/${appId}/tenants`);
  }

  createTenant(appId: string, name: string): Promise<void> {
    return this.api.post(`admin/applications/${appId}/tenants`, { name });
  }

  patchTenant(appId: string, tenantId: string, body: PatchTenantRequest): Promise<void> {
    return this.api.patch(`admin/applications/${appId}/tenants/${tenantId}`, body);
  }

  deleteTenant(appId: string, tenantId: string): Promise<void> {
    return this.api.delete(`admin/applications/${appId}/tenants/${tenantId}`);
  }

  // Teams
  getTeams(appId: string, tenantId: string): Promise<TeamSummary[]> {
    return this.api.get(`admin/applications/${appId}/tenants/${tenantId}/teams`);
  }

  createTeam(appId: string, tenantId: string, name: string): Promise<void> {
    return this.api.post(`admin/applications/${appId}/tenants/${tenantId}/teams`, { name });
  }

  patchTeam(appId: string, tenantId: string, teamId: string, name: string): Promise<void> {
    return this.api.patch(`admin/applications/${appId}/tenants/${tenantId}/teams/${teamId}`, { name });
  }

  deleteTeam(appId: string, tenantId: string, teamId: string): Promise<void> {
    return this.api.delete(`admin/applications/${appId}/tenants/${tenantId}/teams/${teamId}`);
  }

  // Users
  getUsers(appId: string): Promise<UserSummary[]> {
    return this.api.get(`admin/applications/${appId}/users`);
  }

  /** Creates the user, places them in a team with roles and returns their first invitation link. */
  createUser(appId: string, request: CreateUserRequest): Promise<CreateUserResponse> {
    return this.api.post(`admin/applications/${appId}/users`, request);
  }

  getUser(appId: string, userId: string): Promise<UserDetail> {
    return this.api.get(`admin/applications/${appId}/users/${userId}`);
  }

  patchUser(appId: string, userId: string, body: PatchUserRequest): Promise<void> {
    return this.api.patch(`admin/applications/${appId}/users/${userId}`, body);
  }

  // Members
  getMembers(appId: string, tenantId: string, teamId: string): Promise<MemberSummary[]> {
    return this.api.get(`admin/applications/${appId}/tenants/${tenantId}/teams/${teamId}/members`);
  }

  addMember(appId: string, tenantId: string, teamId: string, userId: string, roleIds: string[]): Promise<void> {
    return this.api.post(`admin/applications/${appId}/tenants/${tenantId}/teams/${teamId}/members`, { userId, roleIds });
  }

  setMemberRoles(appId: string, tenantId: string, teamId: string, memberId: string, roleIds: string[]): Promise<void> {
    return this.api.patch(`admin/applications/${appId}/tenants/${tenantId}/teams/${teamId}/members/${memberId}/roles`, { roleIds });
  }

  removeMember(appId: string, tenantId: string, teamId: string, memberId: string): Promise<void> {
    return this.api.delete(`admin/applications/${appId}/tenants/${tenantId}/teams/${teamId}/members/${memberId}`);
  }

  // Invitations
  createInvitation(userId: string): Promise<InvitationCreatedResponse> {
    return this.api.post(`admin/users/${userId}/invitations`, {});
  }

  getInvitations(userId: string): Promise<InvitationSummary[]> {
    return this.api.get(`admin/users/${userId}/invitations`);
  }

  revokeInvitation(invitationId: string): Promise<void> {
    return this.api.delete(`admin/invitations/${invitationId}`);
  }

  previewInvitation(token: string): Promise<InvitationPreviewResponse> {
    return this.api.get(`invitations/${token}`);
  }

  acceptInvitation(token: string): Promise<InvitationAcceptedResponse> {
    return this.api.post(`invitations/${token}/accept`, {});
  }
}
