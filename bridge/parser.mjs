export function parseRobloxAccount(text) {
  if (typeof text !== 'string' || text.length > 100) return null;
  const raw = text.trim().replace(/^(?:rbx|roblox|join|!join)\s+/i, '').replace(/^@/, '');
  if (/^[1-9]\d{0,14}$/.test(raw)) return raw;
  return /^[A-Za-z0-9_]{3,20}$/.test(raw) ? raw : null;
}
