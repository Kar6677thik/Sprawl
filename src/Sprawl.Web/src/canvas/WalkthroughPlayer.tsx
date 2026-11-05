import type { Walkthrough } from "../api/types";
import { Body } from "./parts";

interface Props {
  walkthrough: Walkthrough;
  regionTitle: string;
  step: number;
  onStep: (step: number) => void;
  onExit: () => void;
}

/**
 * The words beside the picture: the current step's explanation in a caption card, with the step controls under it.
 * Kept next to the diagram rather than in a side document (the split-attention effect).
 */
export function WalkthroughPlayer({ walkthrough, regionTitle, step, onStep, onExit }: Props) {
  const total = walkthrough.steps.length;
  const current = walkthrough.steps[step];
  if (!current) return null;
  return (
    <div className="walkthrough" role="region" aria-label={`Walkthrough: ${walkthrough.title}`}>
      <div className="walkthrough-card" aria-live="polite">
        <p className="walkthrough-where">{regionTitle} · {walkthrough.title}</p>
        <div className="walkthrough-text"><Body markdown={current.text} /></div>
      </div>
      <div className="walkthrough-bar">
        <button type="button" className="icon-button" aria-label="Previous step" disabled={step === 0} onClick={() => onStep(step - 1)}>‹</button>
        <ol className="walkthrough-dots" aria-label="Steps">
          {walkthrough.steps.map((s, i) => (
            <li key={i}>
              <button type="button" className={i === step ? "is-on" : i < step ? "is-done" : ""} aria-label={`Step ${i + 1}`} aria-current={i === step ? "step" : undefined}
                title={s.text.slice(0, 80)} onClick={() => onStep(i)} />
            </li>
          ))}
        </ol>
        <span className="walkthrough-count">{step + 1} / {total}</span>
        {step < total - 1
          ? <button type="button" className="primary small" onClick={() => onStep(step + 1)}>Next <kbd>→</kbd></button>
          : <button type="button" className="primary small" onClick={onExit}>Finish</button>}
        <button type="button" className="quiet small" onClick={onExit}>Close <kbd>Esc</kbd></button>
      </div>
    </div>
  );
}
