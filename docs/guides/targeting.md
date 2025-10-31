
# Targeting Rule Guide

FFaaS rules let you tailor flag values for specific users or cohorts before falling back to the default. Each rule is a `TargetRule` object stored alongside the flag and evaluated in priority order.

## Rule Schema

```json
{
  "attribute": "country",
  "operator": "eq",
  "value": "NL",
  "priority": 1,
  "boolOverride": true,
  "stringOverride": null,
  "numberOverride": null,
  "percentage": null,
  "percentageAttribute": null,
  "segmentDelimiter": ","
}
```

- **priority**: lower numbers run first; `null` values execute last.
- **override** fields apply when the rule matches; missing overrides fall back to the flag default.
- **percentage** controls rollout thresholds (0-100); `percentageAttribute` chooses the attribute used for hashing (defaults to `attribute` or `userId`).
- **segmentDelimiter** defines how multi-value attributes and rule values are split when using the `segment` operator.

## Available Operators

| Operator    | Description                                                                                             |
| ----------- | ------------------------------------------------------------------------------------------------------- |
| `eq`        | Case-insensitive equality check.                                                                        |
| `ne`        | Case-insensitive inequality check.                                                                      |
| `contains`  | Case-insensitive substring check.                                                                       |
| `gt`, `ge`  | Numeric comparison (`greater-than`, `greater-or-equal`).                                                |
| `lt`, `le`  | Numeric comparison (`less-than`, `less-or-equal`).                                                       |
| `regex`     | Case-insensitive regular expression match; invalid patterns fail gracefully.                            |
| `segment` / `in` | Checks whether any attribute segment matches the rule value list using the configured delimiter.   |
| `percentage`| Deterministic rollout based on a hashed attribute/user identifier.                                      |

> **Note:** Numeric operators convert both sides using `InvariantCulture`. Values that cannot be parsed are treated as non-matches.

## Segment Matching

Use `segment` (or `in`) when your attribute contains multiple tags:

```json
{
  "attribute": "segments",
  "operator": "segment",
  "value": "beta,internal",
  "priority": 10,
  "boolOverride": true
}
```

With context:

```json
{
  "userId": "u-1",
  "attributes": {
    "segments": "pilot,beta"
  }
}
```

The evaluator splits both sides by `segmentDelimiter` (`,` by default) and matches on any overlap. Specify a custom delimiter (e.g., `"segmentDelimiter": "|"`) when your attributes use a different separator.

## Percentage Rollouts

Percentage rules enable gradual releases:

```json
{
  "attribute": "userId",
  "operator": "percentage",
  "value": "checkout-experiment",       // salt
  "percentage": 10,
  "numberOverride": 1.0
}
```

- `percentage`: threshold (0-100). Values <=0 never match; values >=100 always match.
- `value`: optional salt to differentiate multiple rollouts under the same flag key.
- `percentageAttribute`: overrides the basis used for hashing (e.g., `"sessionId"`). Defaults to `attribute` or `userId`.

Hashing is stable: the same user and salt produce the same bucket across evaluations, ensuring deterministic rollouts even across instances.

## Evaluation Order

1. Rules are sorted by `priority` (ascending); ties honour list order.
2. The first matching rule stops evaluation and applies its override (falling back to the flag default when overrides are `null`).
3. If no rules match, the flag default value is returned.

## Null & Missing Values

- Missing attributes or null attribute values automatically fail rule matches.
- Regex rules with empty/invalid patterns are ignored.
- Segment rules with empty `value` arrays never match.
- Percentage rules with no viable basis (attribute and `userId` both missing) never match.

## Examples

### Numeric Gradation
```json
[
  { "attribute": "score", "operator": "gt", "value": "900", "priority": 1, "numberOverride": 1 },
  { "attribute": "score", "operator": "gt", "value": "700", "priority": 2, "numberOverride": 0.5 }
]
```

### Regex Email Target
```json
[
  { "attribute": "email", "operator": "regex", "value": @"^[a-z]+@example\.com$", "priority": 1, "boolOverride": true }
]
```

### Combined Segment + Percentage
```json
[
  {
    "attribute": "segments",
    "operator": "segment",
    "value": "beta",
    "priority": 1,
    "boolOverride": true
  },
  {
    "attribute": "userId",
    "operator": "percentage",
    "value": "gradual-rollout",
    "percentage": 10,
    "priority": 2,
    "boolOverride": true
  }
]
```

## Authoring Tips

- Prefer editing rules in JSON (CLI `--rules` files) to ensure new fields are captured.
- Keep rule priorities sparse (e.g., 10, 20, 30) for easier future insertions.
- Combine `segment` and `percentage` rules to seed early adopters while rolling out to the wider population.
- When using regex, keep patterns simple and fast; extremely complex expressions may time out (250 ms limit per evaluation).

## Updating via Admin CLI

```powershell
@'
[
  {
    "attribute": "userId",
    "operator": "percentage",
    "value": "gradual-rollout",
    "percentage": 15,
    "percentageAttribute": "sessionId"
  }
]
'@ | Set-Content rules.json

dotnet run --project tools/FfaasLite.AdminCli -- --api-key <token> flags upsert checkout --rules rules.json
```

The CLI deserialises the JSON into `TargetRule` objects, preserving advanced fields like `percentage`, `percentageAttribute`, and `segmentDelimiter`.

For a walkthrough of CLI-based updates, see the README's **Admin CLI** section.
