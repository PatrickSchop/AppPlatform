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
  private error: string | null = null;

  private constructor(msal: any, scopes: string[], error: string | null) {
    this.msal = msal;
    this.scopes = scopes;
    this.error = error;
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
    // A failed sign-in (consent missing, wrong tenant, ...) must not stop the app from starting;
    // it is kept on the client so the UI can show it instead of looping back to sign-in.
    let redirectResult: any = null;
    let error: string | null = null;
    try {
      redirectResult = await msal.handleRedirectPromise();
    } catch (e: any) {
      error = e?.message || String(e);
    }
    if (redirectResult?.account) {
      msal.setActiveAccount(redirectResult.account);
    } else if (!msal.getActiveAccount()) {
      const accounts = msal.getAllAccounts();
      if (accounts.length > 0) {
        msal.setActiveAccount(accounts[0]);
      }
    }

    const client = new AuthClient(msal, options.scopes, error);
    return client;
  }

  getAccount(): AccountInfo | null {
    return this.account;
  }

  /** Message of a sign-in that failed on return from the identity provider, if any. */
  getError(): string | null {
    return this.error;
  }

  isSignedIn(): boolean {
    return this.account !== null;
  }

  // Redirect, not popup: sign-in is triggered without a user gesture (route guard, 401
  // handler), and browsers block popups opened that way.
  async signIn(): Promise<void> {
    await this.msal.loginRedirect({ scopes: this.scopes });
  }

  /**
   * Forget the account cached in this browser and let the user pick one at the identity
   * provider. For when the remembered account is the wrong one (several Microsoft accounts
   * signed in, or an account whose organisation has not granted access to the app).
   */
  async switchAccount(): Promise<void> {
    this.account = null;
    this.error = null;
    await this.msal.clearCache();
    await this.msal.loginRedirect({ scopes: this.scopes, prompt: 'select_account' });
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
