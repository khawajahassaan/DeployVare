import * as React from "react"
import { cn } from "@/lib/utils"

interface DialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  children: React.ReactNode
}

export function Dialog({ open, onOpenChange, children }: DialogProps) {
  // Close on Escape and stop the page behind from scrolling while open
  React.useEffect(() => {
    if (!open) return

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onOpenChange(false)
    }
    const previousOverflow = document.body.style.overflow

    document.addEventListener("keydown", onKeyDown)
    document.body.style.overflow = "hidden"

    return () => {
      document.removeEventListener("keydown", onKeyDown)
      document.body.style.overflow = previousOverflow
    }
  }, [open, onOpenChange])

  if (!open) return null

  return (
    <div className="fixed inset-0 z-[1000] flex items-center justify-center p-2 sm:p-4" role="dialog" aria-modal="true">
      <div
        className="fixed inset-0 bg-black/45 animate-fade-in"
        onClick={() => onOpenChange(false)}
      />
      {children}
    </div>
  )
}

// AuditVare modal (CustomModal / r3-modal): white, 16px corners, 20px side padding
export function DialogContent({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <div
      className={cn(
        "relative z-10 max-h-[calc(100vh-2rem)] w-full max-w-[580px] overflow-y-auto rounded-[16px] bg-white px-5 pb-5 pt-[15px] shadow-[0_6px_16px_0_rgba(0,0,0,0.08),0_3px_6px_-4px_rgba(0,0,0,0.12),0_9px_28px_8px_rgba(0,0,0,0.05)] animate-dialog-in",
        className
      )}
    >
      {children}
    </div>
  )
}

export function DialogHeader({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return <div className={cn("mb-5 space-y-1", className)}>{children}</div>
}

export function DialogTitle({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <h2 className={cn("text-[18px] font-normal leading-[22px] text-black", className)}>{children}</h2>
  )
}

export function DialogDescription({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <p className={cn("text-[14px] text-label", className)}>{children}</p>
  )
}

export function DialogFooter({
  children,
  className,
}: {
  children: React.ReactNode
  className?: string
}) {
  return (
    <div className={cn("mt-6 flex justify-end gap-2", className)}>{children}</div>
  )
}
