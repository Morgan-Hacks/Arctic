"use client";

import { useRef } from "react";
import { Calendar03Icon } from "@hugeicons/core-free-icons";
import { Icon } from "@/components/ui/icon";
import { Select } from "@/components/ui/select";
import Form from "next/form";
import type { EventSummary } from "./api";
import styles from "./inventory.module.css";

/**
 * Which event's stock this is.
 *
 * A URL parameter, the same as every filter on /applicants: an organizer who
 * spots a shortage can send this exact link to whoever is driving to the
 * store, and a choice held only in React state cannot be sent to anybody.
 */
export function EventPicker({
  events,
  chosen,
}: {
  events: EventSummary[];
  chosen: EventSummary;
}) {
  const form = useRef<HTMLFormElement>(null);

  if (events.length <= 1) {
    return (
      <div className={styles.eventPicker}>
        <Icon icon={Calendar03Icon} size={18} />
        <span>{chosen.name}</span>
      </div>
    );
  }

  return (
    <Form
      ref={form}
      action="/inventory"
      className={styles.eventPicker}
      role="search"
      aria-label="Choose event"
    >
      <Icon icon={Calendar03Icon} size={18} />
      <Select
        aria-label="Event"
        name="eventId"
        defaultValue={chosen.id}
        onChange={() => form.current?.requestSubmit()}
      >
        {events.map((event) => (
          <option key={event.id} value={event.id}>
            {event.name}
          </option>
        ))}
      </Select>
    </Form>
  );
}
