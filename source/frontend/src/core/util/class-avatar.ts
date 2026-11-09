export function classInitials(name: string): string {
  return name.trim().slice(0, 2).toUpperCase();
}

export function classAvatarColor(name: string): string {
  let hash = 2166136261;
  for (let index = 0; index < name.length; index++) {
    hash = Math.imul(hash ^ name.charCodeAt(index), 16777619) >>> 0;
  }

  return `hsl(${hash % 360} 45% 38%)`;
}
