/** Sprawl's mark: three cards joined by connections, the newest one (in the accent) still being added. */
export function Mark({ size = 18 }: { size?: number }) {
  return (
    <svg className="mark" viewBox="0 0 18 18" width={size} height={size} aria-hidden="true">
      <path d="M4.5 5.5 L12.5 4.5 M4.5 5.5 L8 13" fill="none" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
      <rect x="1.5" y="3" width="6" height="5" rx="1.5" fill="currentColor" />
      <rect x="10" y="2" width="6" height="5" rx="1.5" fill="currentColor" />
      <rect className="mark-node" x="5" y="10.5" width="6" height="5" rx="1.5" />
    </svg>
  );
}
