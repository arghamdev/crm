import { existsSync, readFileSync, readdirSync, statSync } from "node:fs";
import { extname, resolve } from "node:path";

const root = resolve(import.meta.dirname, "..");
const ignoredDirectories = new Set(["bin", "obj", "node_modules", ".git"]);
const forbiddenExtensions = new Set([".pfx", ".p12", ".key", ".pem"]);
const failures = [];

for (const file of walk(root)) {
  const extension = extname(file).toLowerCase();
  if (forbiddenExtensions.has(extension)) failures.push("Private credential file must not be committed: " + file);
  if (![".json", ".cs", ".md", ".yml", ".yaml", ".js", ".mjs"].includes(extension)) continue;
  const text = readFileSync(file, "utf8");
  if (/-----BEGIN (?:RSA |EC )?PRIVATE KEY-----/.test(text)) failures.push("Private key material found: " + file);
  if (/\beyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}\b/.test(text)) failures.push("JWT-like value found: " + file);
}

const settingsPath = resolve(root, "src/Crm.Web/appsettings.json");
if (existsSync(settingsPath)) {
  const settings = JSON.parse(readFileSync(settingsPath, "utf8"));
  const clientSecret = settings.Authentication?.Oidc?.ClientSecret ?? "";
  const ipHashSalt = settings.Security?.IpHashSalt ?? "";
  if (clientSecret && !clientSecret.startsWith("replace-")) failures.push("appsettings.json contains a non-placeholder OIDC client secret.");
  if (ipHashSalt && !ipHashSalt.startsWith("demo-") && !ipHashSalt.startsWith("replace-")) failures.push("appsettings.json contains a non-placeholder IP hash salt.");
}

if (failures.length) {
  console.error(failures.join("\n"));
  process.exit(1);
}

console.log("No private keys, JWTs or non-placeholder production credentials were detected.");

function walk(directory) {
  const files = [];
  for (const name of readdirSync(directory)) {
    if (ignoredDirectories.has(name)) continue;
    const path = resolve(directory, name);
    if (statSync(path).isDirectory()) files.push(...walk(path));
    else files.push(path);
  }
  return files;
}
