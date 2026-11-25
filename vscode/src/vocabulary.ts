import type { Descriptor } from './descriptor';

/** Splits PascalCase, camelCase, snake_case and kebab-case into lower case words. */
export function tokens(name: string): string[] {
  return (name ?? '')
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .split(/[^A-Za-z0-9]+/)
    .map(t => t.toLowerCase())
    .filter(Boolean);
}

const group = (icon: string, words: string) => words.split(' ').map(w => [w, icon] as const);

/** Words with one obvious icon. A name that hits one icon needs no model call. */
export const WORDS: ReadonlyMap<string, string> = new Map([
  ...group('mail', 'email mail smtp'),
  ...group('key-round', 'password secret credential credentials'),
  ...group('log-in', 'login logon signin authenticate authentication'),
  ...group('log-out', 'logout signout'),
  ...group('shield-check', 'permission permissions role roles policy policies authorization'),
  ...group('shield', 'security'),
  ...group('lock', 'encrypt encryption decrypt crypto cipher hash'),
  ...group('receipt', 'invoice receipt billing'),
  ...group('credit-card', 'payment checkout'),
  ...group('shopping-cart', 'order cart basket'),
  ...group('package', 'product inventory stock'),
  ...group('truck', 'shipping shipment delivery'),
  ...group('user', 'user account profile'),
  ...group('users', 'member team'),
  ...group('heart-pulse', 'patient'),
  ...group('calendar', 'appointment schedule calendar date'),
  ...group('clock', 'time timer duration'),
  ...group('bell', 'notification alert'),
  ...group('message-square', 'message chat sms'),
  ...group('message-circle', 'comment'),
  ...group('phone', 'phone call'),
  ...group('file-text', 'document pdf'),
  ...group('folder', 'folder directory'),
  ...group('image', 'image photo picture thumbnail'),
  ...group('video', 'video'),
  ...group('volume-2', 'audio sound'),
  ...group('upload', 'upload'),
  ...group('download', 'download'),
  ...group('database', 'database sql db'),
  ...group('scroll-text', 'log logging logger'),
  ...group('search', 'search'),
  ...group('funnel', 'filter'),
  ...group('arrow-up-down', 'sort'),
  ...group('settings', 'settings config configuration options preferences'),
  ...group('map-pin', 'location address geo'),
  ...group('languages', 'language translation locale'),
  ...group('palette', 'theme color colour'),
  ...group('chart-column', 'report chart analytics statistics stats metrics'),
  ...group('printer', 'print printer'),
  ...group('qr-code', 'barcode qr'),
  ...group('link', 'url uri hyperlink'),
  ...group('globe', 'web http'),
  ...group('webhook', 'webhook'),
  ...group('network', 'network'),
  ...group('cloud', 'cloud'),
  ...group('bug', 'bug debug'),
  ...group('dices', 'random'),
  ...group('git-branch', 'version branch'),
  ...group('star', 'rating favorite'),
  ...group('tag', 'tag label'),
  ...group('bookmark', 'bookmark'),
  ...group('ticket-percent', 'discount coupon promo'),
  ...group('circle-dollar-sign', 'price pricing cost'),
  ...group('percent', 'tax'),
  ...group('banknote', 'currency money'),
  ...group('wallet', 'wallet'),
  ...group('landmark', 'bank'),
  ...group('sheet', 'spreadsheet excel csv'),
  ...group('archive', 'archive zip compress'),
  ...group('contact', 'contact'),
  ...group('camera', 'camera'),
  ...group('clipboard', 'clipboard'),
  ...group('list-todo', 'todo task'),
  ...group('sticky-note', 'note'),
  ...group('mic', 'voice speech microphone'),
  ...group('cookie', 'cookie'),
  ...group('calculator', 'calculator calculate'),
  ...group('server', 'server'),
  ...group('inbox', 'inbox'),
]);

/** The first word of a method name gives a small badge. */
export const BADGES: ReadonlyMap<string, string> = new Map([
  ...group('plus', 'create add new insert register'),
  ...group('x', 'delete remove drop clear cancel'),
  ...group('search', 'get find fetch load read query search list'),
  ...group('send', 'send publish post emit dispatch'),
  ...group('check', 'validate verify check ensure assert is has can'),
  ...group('pencil', 'update edit set save write'),
  ...group('refresh-cw', 'sync refresh reload retry'),
]);

const singular = (w: string) => (w.length > 3 && w.endsWith('s') && !w.endsWith('ss') ? w.slice(0, -1) : w);

/** The icon for a name when all its known words agree on one icon. Otherwise null. */
export function localIcon(d: Pick<Descriptor, 'name'>): string | null {
  const icons = new Set<string>();
  for (const t of tokens(d.name)) {
    const icon = WORDS.get(t) ?? WORDS.get(singular(t));
    if (icon) icons.add(icon);
  }
  return icons.size === 1 ? [...icons][0] : null;
}

/** The badge icon for a method name that starts with a known verb. Otherwise null. */
export function badgeFor(d: Pick<Descriptor, 'name' | 'kind'>): string | null {
  if (d.kind !== 'Method') return null;
  const first = tokens(d.name)[0];
  return (first && BADGES.get(first)) || null;
}

const NOISE = new Set(['the', 'and', 'for', 'from', 'with', 'all', 'by', 'to', 'of', 'on', 'or', 'id', 'async',
  'handler', 'helper', 'manager', 'service', 'utility', 'util', 'utils', 'base', 'factory', 'provider',
  'controller', 'impl', 'default', 'internal', 'private', 'public', 'abstract', 'static']);

/** Words that name a domain concept. Verbs, constructs and short words are left out. */
export function conceptWords(name: string): string[] {
  return [...new Set(tokens(name).map(singular))].filter(t => t.length >= 3 && !NOISE.has(t) && !BADGES.has(t));
}
