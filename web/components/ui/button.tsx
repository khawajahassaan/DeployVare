import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "@/lib/utils"

// Mirrors AuditVare's shared Button: 38px, 8px corners, navy primary that turns
// sky blue on hover, and the grey #DDE4E8 / #9DABB9 disabled look instead of fading
const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-[8px] text-[14px] font-normal leading-none transition-colors duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/40 focus-visible:ring-offset-1 disabled:pointer-events-none [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        default:
          "bg-primary text-primary-foreground hover:bg-brand-hover disabled:bg-disabled disabled:text-disabled-foreground",
        destructive:
          "bg-destructive text-destructive-foreground hover:bg-[#BC3119] disabled:bg-disabled disabled:text-disabled-foreground",
        outline:
          "border border-brand-blue bg-transparent text-brand-blue hover:border-brand-hover hover:bg-brand-hover hover:text-white disabled:border-disabled disabled:bg-disabled disabled:text-disabled-foreground",
        secondary:
          "bg-secondary text-secondary-foreground hover:bg-brand-blue hover:text-white disabled:text-disabled-foreground",
        ghost:
          "text-primary hover:bg-accent hover:text-accent-foreground disabled:text-disabled-foreground",
        link: "h-auto px-0 text-brand-blue underline underline-offset-2 hover:text-primary disabled:text-disabled-foreground",
      },
      size: {
        default: "h-[38px] px-3",
        sm: "h-8 px-2.5 text-[13px]",
        lg: "h-10 px-4",
        icon: "size-[38px]",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  }
)

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean
}

const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  // asChild is accepted for API compatibility but not forwarded to the DOM
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  ({ className, variant, size, asChild, ...props }, ref) => {
    return (
      <button
        className={cn(buttonVariants({ variant, size, className }))}
        ref={ref}
        {...props}
      />
    )
  }
)
Button.displayName = "Button"

export { Button, buttonVariants }
