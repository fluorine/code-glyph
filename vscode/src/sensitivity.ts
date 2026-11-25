export type Sensitivity = 'strict' | 'balanced' | 'generous';

export const LIMITS: Record<Sensitivity, number> = { strict: 0.7, balanced: 0.5, generous: 0.4 };

/** The exact limit wins when it is a number above 0. Otherwise the sensitivity level gives the limit. */
export function confidenceLimit(sensitivity: string | undefined, exact: number | null | undefined): number {
  if (typeof exact === 'number' && exact > 0) return exact;
  return LIMITS[sensitivity as Sensitivity] ?? LIMITS.balanced;
}
