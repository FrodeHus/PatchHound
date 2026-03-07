type ExposureScoreProps = {
  score: number
}

export function ExposureScore({ score }: ExposureScoreProps) {
  const roundedScore = Number(score.toFixed(1))

  return (
    <section className="rounded-lg border border-border bg-card p-4">
      <p className="text-sm text-muted-foreground">Exposure Score</p>
      <p className="mt-2 text-4xl font-semibold tracking-tight">{roundedScore}</p>
      <p className="mt-2 text-xs text-muted-foreground">Composite risk metric across open vulnerability-asset pairs.</p>
    </section>
  )
}
