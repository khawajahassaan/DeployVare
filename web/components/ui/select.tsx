import * as React from "react"
import { cn } from "@/lib/utils"

export type SelectProps = React.SelectHTMLAttributes<HTMLSelectElement>

// Native select drawn like AuditVare's Select box: 40px, #CECECE border, #484A4A chevron
const Select = React.forwardRef<HTMLSelectElement, SelectProps>(
  ({ className, children, ...props }, ref) => {
    return (
      <select
        className={cn(
          "flex h-10 w-full appearance-none rounded-[6px] border border-input dv-select-chevron bg-white py-1 pl-[11px] pr-8 text-[14px] text-foreground transition-colors hover:border-[#333333] focus-visible:border-[#333333] focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-60",
          className
        )}
        ref={ref}
        {...props}
      >
        {children}
      </select>
    )
  }
)
Select.displayName = "Select"

export { Select }
