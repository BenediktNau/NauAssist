import { useState, type KeyboardEvent } from "react";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";

interface MessageInputProps {
  onSend: (text: string) => void;
  disabled: boolean;
}

export function MessageInput({ onSend, disabled }: MessageInputProps) {
  const [value, setValue] = useState("");

  const submit = () => {
    const trimmed = value.trim();
    if (!trimmed || disabled) return;
    onSend(trimmed);
    setValue("");
  };

  const onKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      submit();
    }
  };

  return (
    <div className="flex gap-2 items-end p-4 border-t bg-white">
      <Textarea
        value={value}
        onChange={(e) => setValue(e.target.value)}
        onKeyDown={onKeyDown}
        placeholder="Termin-Anfrage hier paste-n oder Nachricht eingeben… (Enter zum Senden, Shift+Enter für Zeilenumbruch)"
        rows={2}
        disabled={disabled}
        className="resize-none"
      />
      <Button onClick={submit} disabled={disabled || !value.trim()}>
        Senden
      </Button>
    </div>
  );
}
