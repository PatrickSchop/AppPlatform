export interface AuthClientOptions {
  tenantId: string;
  clientId: string;
  scopes: string[];
  redirectUri?: string;
}

export interface AccountInfo {
  username: string;
  name?: string;
}

export class AuthClient {
  private msal: any;
  private account: AccountInfo | null = null;

  private constructor(msal: any) {
    this.msal = msal;
    const account = msal.getActiveAccount();
    if (account) {
      this.account = {
        username: account.username,
        name: account.name,
      };
    }
  }

  static async create(options: AuthClientOptions): Promise<AuthClient> {
    const msalModule = await import('@azure/msal-browser');
    const PublicClientApplication = msalModule.PublicClientApplication;
    const InteractionRequiredAuthError = msalModule.InteractionRequiredAuthError;

    const redirectUri = options.redirectUri || window.location.origin;

    const msal = new PublicClientApplication({
      auth: {
        clientId: options.clientId,
        authority: `https://login.microsoftonline.com/${options.tenantId}`,
        redirectUri,
      },
    });

    // Handle redirect promise before returning
    await msal.handleRedirectPromise();

    const client = new AuthClient(msal);
    return client;
  }

  getAccount(): AccountInfo | null {
    return this.account;
  }

  isSignedIn(): boolean {
    return this.account !== null;
  }

  async signIn(): Promise<void> {
    await this.msal.loginPopup();
    const account = this.msal.getActiveAccount();
    if (account) {
      this.account = {
        username: account.username,
        name: account.name,
      };
    }
  }

  async signOut(): Promise<void> {
    await this.msal.logout();
    this.account = null;
  }

  async getAccessToken(): Promise<string | null> {
    const account = this.msal.getActiveAccount();
    if (!account) {
      return null;
    }

    try {
      const response = await this.msal.acquireTokenSilent({
        scopes: this.msal.config.scopes || [],
        account,
      });
      return response.accessToken;
    } catch (error: any) {
      const InteractionRequiredAuthError = (await import('@azure/msal-browser')).InteractionRequiredAuthError;
      if (error instanceof InteractionRequiredAuthError) {
        try {
          const response = await this.msal.acquireTokenRedirect({
            scopes: this.msal.config.scopes || [],
            account,
          });
          return response?.accessToken || null;
        } catch {
          return null;
        }
      }
      return null;
    }
  }
}
