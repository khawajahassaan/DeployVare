import { Loader2 } from "lucide-react"
import { cn } from "@/lib/utils"

interface LoadingStateProps {
  label?: string
  className?: string
}

export function LoadingState({ label = "Loading...", className }: LoadingStateProps) {
  return (
    <div className={cn("flex flex-col items-center justify-center gap-3 py-16 text-center", className)} role="status">
      <Loader2 className="size-6 animate-spin text-brand-blue" />
      <p className="text-[14px] text-label">{label}</p>
    </div>
  )
}
