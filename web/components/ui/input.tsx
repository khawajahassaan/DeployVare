import * as React from "react"
import { cn } from "@/lib/utils"

export type InputProps = React.InputHTMLAttributes<HTMLInputElement>

// AuditVare form field: 40px, #CECECE border that darkens to #333 on hover/focus, no glow
const Input = React.forwardRef<HTMLInputElement, InputProps>(
  ({ className, type, ...props }, ref) => {
    return (
      <input
        type={type}
        className={cn(
          "flex h-10 w-full rounded-[6px] border border-input bg-white px-[11px] py-1 text-[14px] text-foreground transition-colors file:mr-3 file:border-0 file:bg-transparent file:text-[14px] file:font-normal file:text-primary placeholder:text-[#BFBFBF] hover:border-[#333333] focus-visible:border-[#333333] focus-visible:outline-none disabled:cursor-not-allowed disabled:border-input disabled:bg-white disabled:text-foreground disabled:opacity-60",
          className
        )}
        ref={ref}
        {...props}
      />
    )
  }
)
Input.displayName = "Input"

export { Input }
