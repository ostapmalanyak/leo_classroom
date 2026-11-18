import { backendApiBaseUrl } from '../auth/auth-config';

/**
 * The icon of a course is drawn by the backend from a seed, so every list that shows a
 * course shows the same icon. A course without a seed of its own falls back to its id,
 * which keeps the icon stable instead of random.
 */
export function courseIconUrl(seed: string): string {
  return `${backendApiBaseUrl()}/courses/avatar?seed=${encodeURIComponent(seed)}`;
}

/** The seed of a course, falling back to its id when the course has none. */
export function courseIconSeed(iconSeed: string | null | undefined, courseId: number): string {
  return iconSeed ?? `legacy-${courseId}`;
}
