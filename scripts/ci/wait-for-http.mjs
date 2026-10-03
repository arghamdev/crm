const [url, timeoutText = "90"] = process.argv.slice(2);
if (!url) {
  console.error("Usage: node wait-for-http.mjs <url> [timeout-seconds]");
  process.exit(2);
}

const timeoutSeconds = Number(timeoutText);
if (!Number.isFinite(timeoutSeconds) || timeoutSeconds <= 0) {
  console.error("timeout-seconds must be a positive number.");
  process.exit(2);
}

const deadline = Date.now() + timeoutSeconds * 1000;
let lastError = "No response received.";
while (Date.now() < deadline) {
  try {
    const response = await fetch(url, { redirect: "manual", signal: AbortSignal.timeout(5000) });
    if (response.ok) {
      console.log(`Ready: ${url} (${response.status})`);
      process.exit(0);
    }
    lastError = `HTTP ${response.status}`;
  } catch (error) {
    lastError = error instanceof Error ? error.message : String(error);
  }
  await new Promise(resolve => setTimeout(resolve, 2000));
}

console.error(`Timed out waiting for ${url}: ${lastError}`);
process.exit(1);

