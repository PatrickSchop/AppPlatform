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
    this.fetchImpl = options.fetch ?? globalThis.fetch;
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

  async delete<T>(path: string, init?: RequestInit): Promise<T> {
    return this.request<T>(path, { ...init, method: 'DELETE' });
  }

  private async request<T>(path: string, init?: RequestInit): Promise<T> {
    const token = await this.getAccessToken();
    const headers = new Headers(init?.headers);

    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
    }
    headers.set('Content-Type', 'application/json');

    const response = await this.fetchImpl(`${this.baseUrl}${path}`, {
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

      let body: unknown;
      try {
        body = await response.json();
      } catch {
        body = await response.text();
      }

      throw new ApiError(response.status, body);
    }

    return response.json();
  }
}
