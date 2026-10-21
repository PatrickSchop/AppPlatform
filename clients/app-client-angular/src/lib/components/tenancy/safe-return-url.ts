/**
 * A return URL from the query string is only followed when it stays inside the app: a path that
 * starts with a single slash. Anything else (another site, a protocol-relative URL) becomes '/'.
 */
export function safeReturnUrl(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) {
    return '/';
  }
  return value;
}
