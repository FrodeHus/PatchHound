import * as React from "react"

import { cn } from "@/lib/utils"

function InsetPanel({
  as,
  className,
  emphasis = "default",
  ...props
}: React.ComponentPropsWithoutRef<"div"> & {
  as?: React.ElementType
  emphasis?: "default" | "subtle" | "strong"
}) {
  const Component = as ?? "div"

  return (
    <Component
      data-slot="inset-panel"
      data-emphasis={emphasis}
      className={cn(
        "rounded-xl border text-foreground",
        emphasis === "strong"
          ? "border-border/85 bg-card"
          : emphasis === "subtle"
            ? "border-border/70 bg-muted/50"
            : "border-border/80 bg-muted/60",
        className,
      )}
      {...props}
    />
  )
}

export { InsetPanel }
