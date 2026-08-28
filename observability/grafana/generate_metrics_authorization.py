#!/usr/bin/env python3
"""Build the Grafana Cloud OTLP Authorization value for Azure Key Vault.

The metrics publisher token is read with a hidden prompt so it is not placed in
shell history. The result is either printed once for pasting into Azure or,
with --copy, placed on the Windows clipboard without being displayed.
"""

from __future__ import annotations

import argparse
import base64
import getpass
import os
import subprocess
import sys


def build_authorization_value(stack_id: str, publisher_token: str) -> str:
    """Return the exact OTLP HTTP Authorization header value."""
    credentials = f"{stack_id}:{publisher_token}"
    encoded = base64.b64encode(credentials.encode("ascii")).decode("ascii")
    return f"Basic {encoded}"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Generate a Grafana Cloud OTLP metrics Authorization value."
    )
    parser.add_argument(
        "--stack-id",
        required=True,
        help="Grafana Cloud stack ID (for example, 1804691).",
    )
    parser.add_argument(
        "--copy",
        action="store_true",
        help="Copy the generated value to the Windows clipboard instead of printing it.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    stack_id = args.stack_id.strip()
    if not stack_id.isdecimal():
        print("error: --stack-id must contain digits only.", file=sys.stderr)
        return 2

    publisher_token = getpass.getpass(
        "Grafana Cloud metrics publisher token (glc_..., input hidden): "
    ).strip()
    if not publisher_token:
        print("error: a publisher token is required.", file=sys.stderr)
        return 2
    if publisher_token.startswith("Basic "):
        print(
            "error: enter the raw glc_ publisher token, not a Basic authorization value.",
            file=sys.stderr,
        )
        return 2
    if not publisher_token.startswith("glc_"):
        print(
            "error: the metrics publisher token must start with 'glc_'.",
            file=sys.stderr,
        )
        return 2

    authorization_value = build_authorization_value(stack_id, publisher_token)

    if args.copy:
        if os.name != "nt":
            print("error: --copy is supported on Windows only.", file=sys.stderr)
            return 2
        subprocess.run(["clip.exe"], input=authorization_value, text=True, check=True)
        print("Copied the Key Vault value to the clipboard. Paste it into the secret value field.")
        return 0

    print("Paste this complete value into TransitJazzWorkerMetricsPublisherToken:")
    print(authorization_value)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
