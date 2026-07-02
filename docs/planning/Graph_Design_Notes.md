# Graph Design Notes

**Jacob Nuttall**  
June 2026

## 1. Bayes Theorem

### 1.1 Core Theorem & Posterior

- $P(A|B) = \frac{P(B|A)P(A)}{P(B)}$
- Describes probability of A given B
- $P(A|B)$
  - Updated belief after evidence

### 1.2 Component Definitions

- **Prior probability:** $P(A)$
  - The probability of event A over the entire sample space (the universe of possible outcomes), before restricting that universe based on knowing B
- **Probability (Likelihood):** $P(B|A)$
  - How likely is the evidence to be true given that hypothesis (A) is true
- **Evidence:** $P(B)$
  - Likelihood evidence is true over all possible explanations (universe set)
  - $P(B) = P(B|A)P(A) + P(B|\neg A)P(\neg A)$

## 2. LogicLikely Framing of Bayes

### 2.1 Base Mapping

- Let C = claim is true and E = supporting evidence exists
- $P(C|E) = \frac{P(E|C)P(C)}{P(E)}$
- $P(E|C)$ is the probability of evidence, E, existing if C is true
- $P(E) = P(E|C)P(C) + P(E|\neg C)P(\neg C)$

### 2.2 Evaluating Evidence Strength

- Evidence is strong when $P(E|C) \gg P(E|\neg C)$ because this is saying "the probability evidence exists in the sample space where when the claim is already true is significantly greater than the probability the evidence is true in the sample space where the claim is already false"
- If you want to answer the question "Does my evidence appear much more often when the hypothesis is true than false?", so need formula for $P(B_i|\neg A)$ (B is evidence A is hypothesis)
  - $P(B_i|A)$ not necessarily equal to $1 - P(B_i|\neg A)$
  - $P(B_i|A)$ is equal to $1 - P(\neg B_i|A)$ (negation must be event side, not condition side)

### 2.3 Multi-Evidence & Naive Bayes

- For multiple pieces of evidence, we are considering $P(E_1, E_2, ..., E_n)$
- Evidence is conditionally independent (wouldn't want junk evidence being created to discredit more credible sources)
- Because evidence is conditionally independent:

  $$P(B_1, B_2, ..., B_n|A) = P(B_1|A)P(B_2|A)...P(B_n|A) = \prod_i P(B_i|A)$$

- Naive Bayes:

  $$P(A|B_1, ..., B_n) = \frac{P(A)\prod_i P(B_i|A)}{P(B_1, ..., B_n)}$$

  - Naive assumption is that all pieces of evidence are conditionally independent once you know the hypothesis is true
  - Works well enough for LogicLikely, because we do not want evidence too be closely correlated

### 2.4 Odds Formulation

- **Prior Odds:** $O(A) = \frac{P(A)}{P(\neg A)}$
  - How much more likely is A than $\neg A$? (starting probability)
- **Likelihood Ratio:** $LR = \frac{P(B|A)}{P(B|\neg A)}$
  - How strongly the evidence favors the hypothesis being vs it being false
  - $LR > 1$ means evidence is more likely if claim is true
- **Posterior Odds:** $O(A|B) = O(A) \times LR$
  - Updated odds after considering evidence

$$
O(A|B) = O(A) * LR(B)
$$

$$
\Rightarrow \frac{P(A|B)}{1 - P(A|B)} = O(A) * LR(B)
$$

$$
\Rightarrow P(A|B) = [O(A) * LR(B)][1 - P(A|B)]
$$

$$
\Rightarrow P(A|B) = [O(A) * LR(B)]\left[1 - \frac{O(A|B)}{1 + O(A|B)}\right]
$$

.....

$$
\Rightarrow P(A|B) = \frac{O(A) * LR(B)}{1 + O(A)LR(B)}
$$
