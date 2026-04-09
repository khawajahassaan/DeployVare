import React from 'react';
import { cn } from '@/lib/utils';

interface DiffViewerProps {
  changes: string[];
  className?: string;
}

export function DiffViewer({ changes, className }: DiffViewerProps) {
  return (
    <div className={cn('rounded-lg border bg-muted/30 p-4 overflow-x-auto', className)}>
      <pre className="text-xs font-mono">
        {changes.map((line, idx) => {
          const isAdded = line.startsWith('+');
          const isRemoved = line.startsWith('-');
          const isUnchanged = line.startsWith(' ');

          return (
            <div
              key={idx}
              className={cn(
                'px-2 py-0.5',
                isAdded && 'bg-green-100 dark:bg-green-950/30 text-green-800 dark:text-green-200',
                isRemoved && 'bg-red-100 dark:bg-red-950/30 text-red-800 dark:text-red-200',
                isUnchanged && 'text-muted-foreground'
              )}
            >
              {line}
            </div>
          );
        })}
      </pre>
    </div>
  );
}




