export type RecentWindowOption = {
  label: string
  value: number
}

export const recentWindowOptions: ReadonlyArray<RecentWindowOption> = [
  { label: 'Last 24 hours', value: 24 },
  { label: 'Last 48 hours', value: 48 },
  { label: 'Last 3 days', value: 72 },
  { label: 'Last 7 days', value: 168 },
  { label: 'Last 30 days', value: 720 },
]

export function recentWindowLabel(value: number | ''): string {
  return recentWindowOptions.find((option) => option.value === value)?.label ?? ''
}
