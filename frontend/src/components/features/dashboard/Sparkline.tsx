import { useMemo } from 'react'

type SparklineProps = {
  data: number[]
  width?: number
  height?: number
  strokeColor?: string
  fillColor?: string
  strokeWidth?: number
  className?: string
}

export function Sparkline({
  data,
  width = 80,
  height = 24,
  strokeColor = 'var(--color-primary)',
  fillColor,
  strokeWidth = 1.5,
  className,
}: SparklineProps) {
  const path = useMemo(() => {
    if (data.length < 2) return null

    const min = Math.min(...data)
    const max = Math.max(...data)
    const range = max - min || 1
    const padding = 1

    const points = data.map((value, index) => ({
      x: padding + (index / (data.length - 1)) * (width - padding * 2),
      y: padding + (1 - (value - min) / range) * (height - padding * 2),
    }))

    const linePath = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${p.x},${p.y}`).join(' ')

    const areaPath = fillColor
      ? `${linePath} L${points[points.length - 1].x},${height} L${points[0].x},${height} Z`
      : null

    return { linePath, areaPath }
  }, [data, width, height, fillColor])

  if (!path) return null

  return (
    <svg
      width={width}
      height={height}
      viewBox={`0 0 ${width} ${height}`}
      className={className}
      fill="none"
    >
      {path.areaPath ? (
        <path d={path.areaPath} fill={fillColor} opacity={0.15} />
      ) : null}
      <path
        d={path.linePath}
        stroke={strokeColor}
        strokeWidth={strokeWidth}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}
