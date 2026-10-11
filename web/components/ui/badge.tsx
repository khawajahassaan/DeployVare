import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "@/lib/utils"

const badgeVariants = cva(
  "inline-flex items-center gap-1 whitespace-nowrap rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset transition-colors",
  {
    variants: {
      variant: {
        default:
          "bg-blue-50 text-blue-700 ring-blue-600/20",
        secondary:
          "bg-slate-100 text-slate-700 ring-slate-500/15",
        destructive:
          "bg-red-50 text-red-700 ring-red-600/20",
        outline:
          "bg-transparent text-slate-700 ring-slate-300",
        success:
          "bg-emerald-50 text-emerald-700 ring-emerald-600/20",
        warning:
          "bg-amber-50 text-amber-800 ring-amber-600/25",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  }
)

export interface BadgeProps
  extends React.HTMLAttributes<HTMLDivElement>,
    VariantProps<typeof badgeVariants> {}

function Badge({ className, variant, ...props }: BadgeProps) {
  return (
    <div className={cn(badgeVariants({ variant }), className)} {...props} />
  )
}

export { Badge, badgeVariants }
