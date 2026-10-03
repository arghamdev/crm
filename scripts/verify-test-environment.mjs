import { isAbsolute } from "node:path";

const required = [
  "CRM_TEST_AUTHORITY",
  "CRM_TEST_CLIENT_ID",
  "CRM_TEST_CLIENT_SECRET",
  "CRM_TEST_IP_HASH_SALT",
  "CRM_TEST_KEY_RING_PATH"
];

const failures = [];
for (const name of required) {
  if (!process.env[name]?.trim()) failures.push("Missing environment variable: " + name);
}

const authority = process.env.CRM_TEST_AUTHORITY ?? "";
if (authority && (!authority.startsWith("https://") || authority.includes("example.test"))) {
  failures.push("CRM_TEST_AUTHORITY must be a real HTTPS test issuer.");
}

const keyRingPath = process.env.CRM_TEST_KEY_RING_PATH ?? "";
if (keyRingPath && !isAbsolute(keyRingPath)) failures.push("CRM_TEST_KEY_RING_PATH must be absolute.");

for (const secretName of ["CRM_TEST_CLIENT_SECRET", "CRM_TEST_IP_HASH_SALT"]) {
  const value = process.env[secretName] ?? "";
  if (value.startsWith("replace-") || value.startsWith("demo-")) failures.push(secretName + " still contains a placeholder.");
}

if (failures.length) {
  console.error(failures.join("\n"));
  process.exit(1);
}

console.log("OIDC test environment variables and shared key-ring path are ready.");
