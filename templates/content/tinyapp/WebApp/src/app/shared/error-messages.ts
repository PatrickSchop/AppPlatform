const errorCodeMap: Record<string, string> = {
  // Add the error codes your API returns and the wording users should see.
};

/** A short, user-facing description of a failed lookup, for the error page. */
export function describeFailure(error: unknown): string {
  const err = error as any;
  const detail = typeof err?.body?.error === 'string' ? ` (${err.body.error})` : '';
  if (err?.status) {
    return `The API returned HTTP ${err.status}${detail}.`;
  }
  return err?.message || 'The API could not be reached.';
}

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
