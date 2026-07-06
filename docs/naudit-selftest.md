# Naudit Self-Test

Dieser PR dient ausschließlich dazu, die Naudit-Code-Review-Pipeline
(Claude-Code-Provider auf der Coolify-Instanz `vita.monocircuit.net`)
end-to-end zu testen. Kann nach dem Test geschlossen/gelöscht werden.

Beispiel-Snippet (bewusst mit einer kleinen Unsauberkeit, damit der
Reviewer etwas zu bewerten hat):

```bash
#!/usr/bin/env bash
target=$1
echo "Räume $target auf ..."
find $target -name '*.tmp' -delete
```
