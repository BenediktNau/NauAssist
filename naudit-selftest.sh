#!/usr/bin/env bash
# Wegwerf-Datei nur für den Naudit-GitHub-App-Selbsttest – bitte NICHT mergen.
# Enthält absichtliche Mängel, damit der automatische Review etwas zu melden hat.

find_and_delete() {
  target=$1
  find $target -name "*.tmp" -exec rm {} \;
  echo "cleaned up $target"
}

find_and_delete /var/tmp
