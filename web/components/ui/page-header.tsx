import * as React from "react"
import type { LucideIcon } from "lucide-react"
import { cn } from "@/lib/utils"

interface PageHeaderProps {
  title: string
  description?: React.ReactNode
  icon?: LucideIcon
  // Buttons shown on the right (stack below the title on small screens)
  actions?: React.ReactNode
  className?: string
}

// AuditVare page title: the module glyph on a white tile beside regular-weight #242424 text
export function PageHeader({ title, description, icon: Icon, actions, className }: PageHeaderProps) {
  return (
    <div className={cn("flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between", className)}>
      <div className="flex min-w-0 items-center gap-3">
        {Icon && (
          <div className="hidden size-[38px] shrink-0 items-center justify-center rounded-[8px] bg-white shadow-[0_1px_1px_rgba(23,33,51,0.05),0_1px_4px_-1px_rgba(23,33,51,0.07)] sm:flex">
            <Icon className="size-5" stroke="url(#dv-brand-gradient)" strokeWidth={1.75} />
          </div>
        )}
        <div className="min-w-0">
          <h1 className="text-[20px] font-normal leading-tight text-ink">{title}</h1>
          {description && <p className="mt-0.5 text-[14px] text-label">{description}</p>}
        </div>
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2 sm:shrink-0">{actions}</div>}
    </div>
  )
}
