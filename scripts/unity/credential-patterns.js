"use strict";

/**
 * The one list of credential shapes this repository looks for in Unity build output.
 *
 * `redact-unity-artifacts.js` is the consumer. It rewrites every match before a Unity output tree
 * is uploaded as a CI artifact. Any future consumer that must refuse credential material -- a
 * release-asset gate, for example -- reads this list instead of restating the shapes, so the two
 * cannot drift apart.
 *
 * Each entry redacts to `<redacted:id>`. When `prefixGroup` is set, capture group 1 is the label to
 * keep so a log line still says which credential was removed, and only the value is destroyed. No
 * placeholder can match any pattern here, so redaction is idempotent.
 */
const CREDENTIAL_PATTERNS = Object.freeze([
  {
    id: "pem-private-key",
    description: "a PEM private key",
    // Prefer the whole block, but a header with no terminator still counts: the bytes after it
    // are key material, so redacting only the header would leave the key readable. A PEM header
    // in Unity build output is never legitimate, so consuming the remainder is the safe choice.
    pattern:
      /-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----[\s\S]*?-----END (?:[A-Z ]+ )?PRIVATE KEY-----|-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----[\s\S]*/
  },
  {
    id: "unity-license-id",
    description: "a Unity license identifier",
    // The value class excludes "<" so this cannot match its own placeholder. Without that, a
    // correctly scrubbed unity.log would keep reporting a hit and could never be sealed.
    pattern: /(<License\b[^>]*\bid=")[^"<]+/,
    prefixGroup: 1
  },
  {
    id: "unity-serial",
    description: "a Unity serial",
    pattern: /\bS[CBP]-[0-9A-Z]{4}(?:-[0-9A-Z]{4}){4}\b/
  },
  {
    id: "github-token",
    description: "a GitHub token",
    pattern: /\b(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,})\b/
  },
  {
    id: "aws-access-key-id",
    description: "an AWS access key id",
    pattern: /\bAKIA[0-9A-Z]{16}\b/
  },
  {
    id: "http-bearer-token",
    description: "an HTTP bearer token",
    pattern: /(\bBearer\s+)[A-Za-z0-9._~+/=-]{20,}/,
    prefixGroup: 1
  },
  {
    id: "credential-assignment",
    description: "a credential assignment",
    // The value must look like real credential material. A masked `TOKEN=***` in a CI log is not a
    // leak, and failing on one would train operators to bypass this check.
    pattern:
      /(\b(?:UNITY_(?:SERIAL|EMAIL|PASSWORD)|[A-Z][A-Z0-9_]*(?:TOKEN|SECRET|PASSWORD|API_KEY))["']?\s*[=:]\s*["']?)[A-Za-z0-9._~+/=@-]{12,}/,
    prefixGroup: 1
  }
]);

/** A file with a NUL byte in its scanned window is treated as binary and left alone. */
function looksBinary(bytes) {
  return bytes.includes(0);
}

function globalRegExp(entry) {
  return new RegExp(entry.pattern.source, `${entry.pattern.flags}g`);
}

/** Every credential kind present in `text`, in declaration order. */
function findCredentials(text) {
  return CREDENTIAL_PATTERNS.filter((entry) => entry.pattern.test(text));
}

/**
 * Replace every credential value in `text`. Returns the rewritten text and a count per pattern id
 * so a caller can report what it removed without ever echoing what it removed.
 */
function redactCredentials(text) {
  const counts = new Map();
  let redacted = text;
  for (const entry of CREDENTIAL_PATTERNS) {
    let replaced = 0;
    redacted = redacted.replace(globalRegExp(entry), (...match) => {
      replaced += 1;
      const prefix = entry.prefixGroup ? match[entry.prefixGroup] : "";
      return `${prefix}<redacted:${entry.id}>`;
    });
    if (replaced > 0) {
      counts.set(entry.id, replaced);
    }
  }
  return { redacted, counts };
}

module.exports = {
  CREDENTIAL_PATTERNS,
  findCredentials,
  looksBinary,
  redactCredentials
};
