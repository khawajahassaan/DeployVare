import * as React from "react"
import type { LucideIcon } from "lucide-react"
import { cn } from "@/lib/utils"

interface EmptyStateProps {
  icon?: LucideIcon
  title: string
  description?: React.ReactNode
  action?: React.ReactNode
  // Smaller padding for use inside a card or section
  compact?: boolean
  className?: string
}

export function EmptyState({ icon: Icon, title, description, action, compact = false, className }: EmptyStateProps) {
  return (
    <div
      className={cn(
        "flex flex-col items-center justify-center rounded-[10px] border border-dashed border-[#CECECE] bg-white/60 px-6 text-center",
        compact ? "py-8" : "py-14",
        className
      )}
    >
      {Icon && (
        <div className="mb-4 flex size-12 items-center justify-center rounded-full bg-accent">
          <Icon className="size-6 text-brand-blue" strokeWidth={1.5} />
        </div>
      )}
      <h3 className="text-[16px] font-normal text-ink">{title}</h3>
      {description && <p className="mt-1 max-w-md text-[14px] text-label">{description}</p>}
      {action && <div className="mt-5 flex flex-wrap items-center justify-center gap-2">{action}</div>}
    </div>
  )
}
