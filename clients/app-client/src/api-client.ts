export interface ApiClientOptions {
  baseUrl: string;
  getAccessToken?: () => Promise<string | null>;
  fetch?: typeof fetch;
  onUnauthorized?: () => void;
}

export class ApiError extends Error {
  readonly status: number;
  readonly body: unknown;

  constructor(status: number, body: unknown) {
    super(`API error ${status}`);
    this.status = status;
    this.body = body;
  }
}

export class ApiClient {
  private baseUrl: string;
  private getAccessToken: () => Promise<string | null>;
  private fetchImpl: typeof fetch;
  private onUnauthorized: () => void;

  constructor(options: ApiClientOptions) {
    this.baseUrl = options.baseUrl;
    this.getAccessToken = options.getAccessToken ?? (() => Promise.resolve(null));
    this.fetchImpl = options.fetch ?? ((input, init) => globalThis.fetch(input, init));  // not a bare reference: fetch throws 'Illegal invocation' when called as a method of this
    this.onUnauthorized = options.onUnauthorized ?? (() => {});
  }

  async get<T>(path: string, init?: RequestInit): Promise<T> {
    return this.request<T>(path, { ...init, method: 'GET' });
  }

  async post<T>(path: string, body?: unknown, init?: RequestInit): Promise<T> {
    return this.request<T>(path, { ...init, method: 'POST', body: body ? JSON.stringify(body) : undefined });
  }

  async put<T>(path: string, body?: unknown, init?: RequestInit): Promise<T> {
    return this.request<T>(path, { ...init, method: 'PUT', body: body ? JSON.stringify(body) : undefined });
  }

  async patch<T>(path: string, body?: unknown, init?: RequestInit): Promise<T> {
    return this.request<T>(path, { ...init, method: 'PATCH', body: body ? JSON.stringify(body) : undefined });
  }

  async delete<T>(path: string, init?: RequestInit): Promise<T> {
    return this.request<T>(path, { ...init, method: 'DELETE' });
  }

  /** Joins baseUrl and path with exactly one slash, whether or not either side has one. */
  private url(path: string): string {
    return `${this.baseUrl.replace(/\/+$/, '')}/${path.replace(/^\/+/, '')}`;
  }

  private async request<T>(path: string, init?: RequestInit): Promise<T> {
    const token = await this.getAccessToken();
    const headers = new Headers(init?.headers);

    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
    }
    headers.set('Content-Type', 'application/json');

    const response = await this.fetchImpl(this.url(path), {
      ...init,
      headers,
    });

    if (response.status === 204) {
      return undefined as T;
    }

    if (!response.ok) {
      if (response.status === 401) {
        this.onUnauthorized();
      }

      // A body can be read once: take the text, then try to interpret it as JSON.
      const text = await response.text().catch(() => '');
      let body: unknown = text;
      try {
        body = JSON.parse(text);
      } catch {
        // not JSON; keep the raw text
      }

      throw new ApiError(response.status, body);
    }

    // Many endpoints answer success with an empty 200; that is a result, not a parse error.
    const text = await response.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }
}
