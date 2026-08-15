import { readFile } from 'node:fs/promises'
import path from 'node:path'

export const assertKnownArguments = (
  args,
  { valueFlags = [], booleanFlags = [] },
) => {
  const valueFlagSet = new Set(valueFlags)
  const booleanFlagSet = new Set(booleanFlags)
  const seen = new Set()

  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index]
    if (!valueFlagSet.has(argument) && !booleanFlagSet.has(argument)) {
      throw new Error(`Unknown argument: ${argument}`)
    }
    if (seen.has(argument)) {
      throw new Error(`${argument} may be supplied only once.`)
    }
    seen.add(argument)

    if (valueFlagSet.has(argument)) {
      const value = args[index + 1]
      if (!value || value.startsWith('--')) {
        throw new Error(`${argument} requires a path.`)
      }
      index += 1
    }
  }
}

export const argumentValue = (args, flag) => {
  const positions = args
    .map((argument, index) => (argument === flag ? index : -1))
    .filter((index) => index >= 0)

  if (positions.length > 1) {
    throw new Error(`${flag} may be supplied only once.`)
  }
  if (positions.length === 0) return null

  const value = args[positions[0] + 1]
  if (!value || value.startsWith('--')) {
    throw new Error(`${flag} requires a path.`)
  }
  return path.resolve(process.cwd(), value)
}

const invariant = (condition, message) => {
  if (!condition) throw new Error(message)
}

const allowedRejectionReasons = new Set(['overt-term', 'graphic-content'])

export const loadSensitiveContentPolicy = async (
  policyPath,
  { requireComplete = true } = {},
) => {
  let bytes
  try {
    bytes = await readFile(policyPath)
  } catch (error) {
    if (error?.code === 'ENOENT') {
      throw new Error(
        `Sensitive-content policy was not found at ${policyPath}. ` +
          'Create the ignored tools/stress-corpus/sensitive-content-policy.local.json using ' +
          'sensitive-content-policy.example.json as the schema guide.',
      )
    }
    throw error
  }

  let policy
  try {
    policy = JSON.parse(bytes.toString('utf8'))
  } catch (error) {
    throw new Error(
      `Invalid JSON in sensitive-content policy ${policyPath}: ${error.message}`,
    )
  }

  invariant(
    policy && typeof policy === 'object' && !Array.isArray(policy),
    `Sensitive-content policy ${policyPath} must be a JSON object.`,
  )
  invariant(
    policy.schemaVersion === 1,
    `Sensitive-content policy ${policyPath} schemaVersion must be 1.`,
  )
  invariant(
    typeof policy.complete === 'boolean',
    `Sensitive-content policy ${policyPath} must declare complete as a boolean.`,
  )
  invariant(
    !requireComplete || policy.complete,
    `Sensitive-content policy ${policyPath} is an incomplete schema example and cannot be used for corpus generation or editorial validation.`,
  )
  invariant(
    Array.isArray(policy.groups) && policy.groups.length > 0,
    `Sensitive-content policy ${policyPath} must define at least one pattern group.`,
  )

  const reasons = new Set()
  const groups = policy.groups.map((group, groupIndex) => {
    invariant(
      group && typeof group === 'object' && !Array.isArray(group),
      `Sensitive-content policy group ${groupIndex} must be an object.`,
    )
    invariant(
      typeof group.rejectionReason === 'string' &&
        allowedRejectionReasons.has(group.rejectionReason),
      `Sensitive-content policy group ${groupIndex} has an invalid rejectionReason.`,
    )
    invariant(
      !reasons.has(group.rejectionReason),
      `Sensitive-content policy rejectionReason ${group.rejectionReason} is duplicated.`,
    )
    reasons.add(group.rejectionReason)
    invariant(
      Array.isArray(group.patterns) && group.patterns.length > 0,
      `Sensitive-content policy group ${group.rejectionReason} must contain patterns.`,
    )

    const patterns = group.patterns.map((source, patternIndex) => {
      invariant(
        typeof source === 'string' && source.length > 0,
        `Pattern ${patternIndex} in ${group.rejectionReason} must have a source.`,
      )
      try {
        return new RegExp(source, 'iu')
      } catch {
        throw new Error(
          `Invalid pattern ${patternIndex} in ${group.rejectionReason}.`,
        )
      }
    })

    return { rejectionReason: group.rejectionReason, patterns }
  })

  invariant(
    !requireComplete || reasons.size === allowedRejectionReasons.size,
    `Sensitive-content policy ${policyPath} must define every required pattern group.`,
  )

  return { path: policyPath, groups }
}

export const sensitiveContentRejectionReason = (text, policy) => {
  for (const group of policy.groups) {
    if (group.patterns.some((pattern) => pattern.test(text))) {
      return group.rejectionReason
    }
  }
  return null
}
