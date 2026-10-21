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
  private scopes: string[];
  private account: AccountInfo | null = null;

  private constructor(msal: any, scopes: string[]) {
    this.msal = msal;
    this.scopes = scopes;
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

    // msal-browser v3 requires initialize() before any other call.
    await msal.initialize();

    // Complete a returning redirect sign-in, then make sure some account is active so that
    // token acquisition works after a reload.
    const redirectResult = await msal.handleRedirectPromise();
    if (redirectResult?.account) {
      msal.setActiveAccount(redirectResult.account);
    } else if (!msal.getActiveAccount()) {
      const accounts = msal.getAllAccounts();
      if (accounts.length > 0) {
        msal.setActiveAccount(accounts[0]);
      }
    }

    const client = new AuthClient(msal, options.scopes);
    return client;
  }

  getAccount(): AccountInfo | null {
    return this.account;
  }

  isSignedIn(): boolean {
    return this.account !== null;
  }

  // Redirect, not popup: sign-in is triggered without a user gesture (route guard, 401
  // handler), and browsers block popups opened that way.
  async signIn(): Promise<void> {
    await this.msal.loginRedirect({ scopes: this.scopes });
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
        scopes: this.scopes,
        account,
      });
      return response.accessToken;
    } catch (error: any) {
      const InteractionRequiredAuthError = (await import('@azure/msal-browser')).InteractionRequiredAuthError;
      if (error instanceof InteractionRequiredAuthError) {
        try {
          const response = await this.msal.acquireTokenRedirect({
            scopes: this.scopes,
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
