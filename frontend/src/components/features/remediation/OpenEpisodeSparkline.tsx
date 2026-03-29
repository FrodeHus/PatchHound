type OpenEpisodeTrendPoint = {
  day: string
  openEpisodeCount: number
}

export function OpenEpisodeSparkline({
  points,
  className = '',
}: {
  points: OpenEpisodeTrendPoint[]
  className?: string
}) {
  if (points.length === 0) {
    return null
  }

  const values = points.map((point) => point.openEpisodeCount)
  const max = Math.max(...values, 1)
  const width = 120
  const height = 28
  const step = points.length > 1 ? width / (points.length - 1) : width
  const linePoints = points
    .map((point, index) => {
      const x = index * step
      const y = height - (point.openEpisodeCount / max) * height
      return `${x},${y}`
    })
    .join(' ')
  const current = points[points.length - 1]?.openEpisodeCount ?? 0
  const peak = Math.max(...values)

  return (
    <div className={`flex items-center gap-2 ${className}`}>
      <svg
        viewBox={`0 0 ${width} ${height}`}
        className="h-7 w-28 shrink-0 overflow-visible"
        aria-hidden="true"
      >
        <path
          d={`M 0 ${height} L ${linePoints} L ${width} ${height} Z`}
          className="fill-primary/10"
        />
        <polyline
          points={linePoints}
          fill="none"
          className="stroke-primary"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>
      <div className="min-w-0 text-[10px] leading-tight text-muted-foreground">
        <div>30d affected devices</div>
        <div>
          now {current} · peak {peak}
        </div>
      </div>
    </div>
  )
}
