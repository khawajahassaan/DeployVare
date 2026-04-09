import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "@/lib/utils"

const badgeVariants = cva(
  "inline-flex items-center rounded-full px-3 py-1 text-xs font-medium transition-all",
  {
    variants: {
      variant: {
        default:
          "bg-blue-500 text-white shadow-sm hover:bg-blue-600",
        secondary:
          "bg-gray-100 text-gray-700 hover:bg-gray-200 dark:bg-gray-800 dark:text-gray-300",
        destructive:
          "bg-red-500 text-white shadow-sm hover:bg-red-600",
        outline: 
          "bg-gray-100 text-gray-700 ring-1 ring-gray-300 hover:bg-gray-200 dark:bg-gray-800 dark:text-gray-300 dark:ring-gray-600",
        success:
          "bg-green-500 text-white shadow-sm hover:bg-green-600",
        warning:
          "bg-yellow-500 text-white shadow-sm hover:bg-yellow-600",
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



