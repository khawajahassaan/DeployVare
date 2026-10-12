import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "@/lib/utils"

// AuditVare StatusBadge colours: status text on a 10% tint of the same colour, 4px corners
const badgeVariants = cva(
  "inline-flex items-center justify-center gap-1 whitespace-nowrap rounded-[4px] px-1.5 py-1 text-[12px] font-normal leading-none transition-colors",
  {
    variants: {
      variant: {
        default:
          "bg-[#0074BA1A] text-[#0F6CBD]",
        secondary:
          "bg-[#3333331A] text-[#333333]",
        destructive:
          "bg-[#C50F1F1A] text-[#C50F1F]",
        outline:
          "border border-[#CECECE] bg-transparent text-[#333333]",
        success:
          "bg-[#00B0501A] text-[#00873D]",
        warning:
          "bg-[#FFC0001A] text-[#9A6B13]",
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
