import { ChronoUnit, DateTimeFormatter, Instant, LocalDate, ZoneId } from '@js-joda/core';
import { DeadlineKind, FeedbackState, SubmissionStatus } from '../services/assignment-service';

export type AssignmentView = 'todo' | 'pushed' | 'past-due';

/** A deadline without a time is due at the end of that day, so `null` is never expired. */
export function isAssignmentExpired(deadline: Instant | null, now: Instant = Instant.now()): boolean {
  return deadline !== null && deadline.isBefore(now);
}

export interface OrderableAssignment {
  title: string;
  deadline: Instant | null;
}

/**
 * One ordering for every assignment list: newest or oldest deadline first, optionally
 * with the title as the primary key, and undated work always last.
 */
export function compareAssignments(
  left: OrderableAssignment,
  right: OrderableAssignment,
  options: { sortByTitle?: boolean; descending?: boolean } = {}
): number {
  if (options.sortByTitle) {
    const byTitle = left.title.localeCompare(right.title);
    if (byTitle !== 0) {
      return byTitle;
    }
  }

  if (left.deadline === null && right.deadline === null) {
    return left.title.localeCompare(right.title);
  }
  if (left.deadline === null) {
    return 1;
  }
  if (right.deadline === null) {
    return -1;
  }

  const byDate = left.deadline.compareTo(right.deadline);

  return options.descending ? -byDate : byDate;
}

export type StudentAssignmentState =
  | 'not-started'
  | 'setting-up'
  | 'in-progress'
  | 'pushed'
  | 'pushed-late'
  | 'past-due'
  | 'failed';

/** A tone names a meaning, never a colour: the token layer decides what each meaning looks like. */
export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'late' | 'danger';

export interface StatePresentation {
  label: string;
  tone: Tone;
}

/** The facts the card needs; a subset of the assignment list row, so it is easy to derive. */
export interface StudentAssignmentFacts {
  accepted: boolean;
  status: SubmissionStatus | null;
  lastPushAt: Instant | null;
  late: boolean;
  deadline: Instant | null;
  feedbackState?: FeedbackState | null;
  deadlineKind?: DeadlineKind | null;
}

/**
 * What the student actually has to do about this assignment. "Ready" used to be shown for
 * every row, which told nobody anything; these are the states that changed what they saw.
 *
 * "Pushed" is deliberately not called "Submitted": the last push before the deadline counts,
 * so a pushed assignment with time left is still editable and saying "submitted" hides that.
 */
export function studentAssignmentState(
  assignment: StudentAssignmentFacts,
  now: Instant = Instant.now()
): StudentAssignmentState {
  if (assignment.lastPushAt !== null) {
    return assignment.late ? 'pushed-late' : 'pushed';
  }

  if (assignment.accepted && assignment.status === SubmissionStatus.Failed) {
    return 'failed';
  }

  // Past due and nothing pushed is missed whether or not the student ever accepted it.
  if (isAssignmentExpired(assignment.deadline, now)) {
    return 'past-due';
  }

  if (!assignment.accepted) {
    return 'not-started';
  }

  if (assignment.status === SubmissionStatus.Provisioning) {
    return 'setting-up';
  }

  if (assignment.status === SubmissionStatus.DownloadOnly) {
    return 'in-progress';
  }

  return assignment.status === SubmissionStatus.Ready ? 'in-progress' : 'setting-up';
}

export function statePresentation(state: StudentAssignmentState): StatePresentation {
  switch (state) {
    case 'pushed':
      return { label: 'Pushed', tone: 'success' };
    case 'pushed-late':
      return { label: 'Pushed late', tone: 'late' };
    case 'past-due':
      return { label: 'Past due', tone: 'danger' };
    case 'failed':
      return { label: 'Setup failed', tone: 'danger' };
    case 'setting-up':
      return { label: 'Setting up', tone: 'info' };
    case 'in-progress':
      // "In progress" claimed work that had not started: nothing was pushed yet.
      return { label: 'Accepted', tone: 'info' };
    default:
      return { label: 'Not started', tone: 'neutral' };
  }
}

/** The three tabs group states, they do not repeat them. */
export function assignmentViewOf(state: StudentAssignmentState): AssignmentView {
  if (state === 'pushed' || state === 'pushed-late') {
    return 'pushed';
  }

  return state === 'past-due' ? 'past-due' : 'todo';
}

/** True while a pushed assignment can still be changed, i.e. the deadline has not passed. */
export function isStillEditable(deadline: Instant | null, now: Instant): boolean {
  return deadline !== null && deadline.isAfter(now);
}

/**
 * The second half of the "Pushed" story: what happens next. Feedback wins over the deadline,
 * because a notification is more useful than a rule the student already knows.
 */
export function submissionNote(facts: StudentAssignmentFacts, now: Instant): string {
  if (facts.feedbackState === FeedbackState.Unread) {
    return 'Feedback available';
  }
  if (facts.feedbackState === FeedbackState.Read) {
    return 'Feedback read';
  }

  // The pill already says "Pushed late"; repeating it in prose is noise. What the student needs
  // from this line is whether the submission can still change, and how long for.
  return isStillEditable(facts.deadline, now)
    ? `Editable · ${relativeDue(facts.deadline, now)}`
    : 'Final — the deadline has passed';
}

/** What a missed deadline means: a soft one can still be handed in, a hard one cannot. */
export function missedPolicy(deadlineKind: DeadlineKind | null | undefined): string {
  return deadlineKind === DeadlineKind.Hard
    ? 'The hard deadline has passed — this can no longer be handed in.'
    : 'The deadline has passed — a late push is still accepted.';
}

export type DueUrgency = 'none' | 'later' | 'soon' | 'warm' | 'imminent' | 'urgent' | 'critical' | 'overdue';

/**
 * How close the deadline is. One scale for every screen, and it ramps: the closer the
 * deadline gets, the more orange the text turns - quiet grey while it is still days away,
 * yellow inside three days, orange inside a day, orange-red inside six hours and a deep
 * orange-red inside the last hour. Red is reserved for a deadline that has already passed:
 * work that is still on time never borrows the colour of missed work.
 */
export function dueUrgency(deadline: Instant | null, now: Instant = Instant.now()): DueUrgency {
  if (deadline === null) {
    return 'none';
  }

  if (deadline.isBefore(now)) {
    return 'overdue';
  }

  const minutesLeft = now.until(deadline, ChronoUnit.MINUTES);
  if (minutesLeft < 60) {
    return 'critical';
  }

  if (minutesLeft < 6 * 60) {
    return 'urgent';
  }

  if (minutesLeft < 12 * 60) {
    return 'imminent';
  }

  if (minutesLeft < 24 * 60) {
    return 'warm';
  }

  return minutesLeft < 72 * 60 ? 'soon' : 'later';
}

/**
 * True when the deadline is still ahead of us but inside the next few hours. Used for the
 * "due in 24 h" counters, which care about the distance and not about the colour.
 */
export function dueWithinHours(deadline: Instant | null, hours: number, now: Instant = Instant.now()): boolean {
  if (deadline === null || deadline.isBefore(now)) {
    return false;
  }

  return now.until(deadline, ChronoUnit.MINUTES) < hours * 60;
}

const timeFormat = DateTimeFormatter.ofPattern('HH:mm');
const zone = ZoneId.systemDefault();

// js-joda core cannot format localized text ("EEE"/"MMM") without the locale plugin, so the
// names come from these tables instead of adding a dependency for two words.
const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const months = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/** "Today, 15:48" / "Tomorrow, 09:00" / "Mon 24 Nov, 09:00" - the day is what people plan by. */
export function dueLabel(deadline: Instant | null): string {
  if (deadline === null) {
    return 'No due date';
  }

  const due = deadline.atZone(zone).toLocalDateTime();
  const today = LocalDate.now(zone);
  const day = due.toLocalDate();
  const time = due.format(timeFormat);

  if (day.equals(today)) {
    return `Today, ${time}`;
  }
  if (day.equals(today.plusDays(1))) {
    return `Tomorrow, ${time}`;
  }
  if (day.equals(today.minusDays(1))) {
    return `Yesterday, ${time}`;
  }

  return `${weekdays[day.dayOfWeek().value() - 1]} ${day.dayOfMonth()} ${months[day.monthValue() - 1]}, ${time}`;
}

/**
 * A past event, always absolute: "Mon 16 Nov, 14:20".
 *
 * Deliberately never "Yesterday": a submission timestamp is evidence, and evidence should not
 * change wording as the clock moves.
 */
export function timestampLabel(instant: Instant | null): string {
  if (instant === null) {
    return '';
  }

  const local = instant.atZone(zone);
  const [hour, minute] = [local.hour(), local.minute()].map(part => String(part).padStart(2, '0'));

  return `${weekdays[local.dayOfWeek().value() - 1]} ${local.dayOfMonth()} ` +
    `${months[local.monthValue() - 1]}, ${hour}:${minute}`;
}

/**
 * "in 5 h", "in 8 days", "34 days ago".
 *
 * Under a day people think in hours, above that in calendar days: 17 Nov to 25 Nov is eight
 * days, not nine, however many hours are in between.
 */
export function relativeDue(deadline: Instant | null, now: Instant = Instant.now()): string {
  if (deadline === null) {
    return '';
  }

  const minutes = now.until(deadline, ChronoUnit.MINUTES);
  const magnitude = Math.abs(minutes);

  if (magnitude < 60) {
    return minutes < 0 ? 'just now' : `in ${Math.max(1, magnitude)} min`;
  }

  if (magnitude < 24 * 60) {
    const hours = Math.floor(magnitude / 60);

    return minutes < 0 ? `${hours} h ago` : `in ${hours} h`;
  }

  const days = calendarDaysBetween(now, deadline);

  if (days === 0) {
    // Only reachable through a clock skew; treating it as "today" beats "in 0 days".
    return minutes < 0 ? 'today' : 'in 24 h';
  }

  const label = `${Math.abs(days)} ${Math.abs(days) === 1 ? 'day' : 'days'}`;

  return days < 0 ? `${label} ago` : `in ${label}`;
}

/**
 * How many exclamation marks a deadline carries. The closer it gets, the louder the row: a
 * deadline days away stays quiet, one inside three days says "!", one inside a day says "!!",
 * inside twelve hours "!!!", inside six hours "!!!!" and inside the last hour "!!!!!". A
 * deadline that has passed shouts loudest of all - that is the state a student has to act on,
 * and the marks make it impossible to scroll past. The missed state stops at ten marks, so a
 * deadline that is years old does not turn the row into a wall of punctuation.
 */
/** The most exclamation marks a missed deadline may carry. */
const OVERDUE_MARKS = 10;

export function urgencyMarks(urgency: DueUrgency): string {
  switch (urgency) {
    case 'soon':
      return '!';
    case 'warm':
      return '!!';
    case 'imminent':
      return '!!!';
    case 'urgent':
      return '!!!!';
    case 'critical':
      return '!!!!!';
    // A deadline that has passed is the one state that has to shout the loudest - but the
    // marks never grow past ten, however long ago it was.
    case 'overdue':
      return '!'.repeat(OVERDUE_MARKS);
    default:
      return '';
  }
}

/** Whole calendar days, so month lengths and DST never leak into a "days left" label. */
export function calendarDaysBetween(from: Instant, to: Instant): number {
  const start = from.atZone(zone).toLocalDate();
  const end = to.atZone(zone).toLocalDate();

  return start.until(end, ChronoUnit.DAYS);
}

export type DueBucket = 'today' | 'tomorrow' | 'next-seven-days' | 'later' | 'none';

/** Buckets exist so a long list can be scanned without a green header per single item. */
export function dueBucket(deadline: Instant | null, now: Instant = Instant.now()): DueBucket {
  if (deadline === null) {
    return 'none';
  }

  const day = deadline.atZone(zone).toLocalDate();
  const today = LocalDate.now(zone);

  if (day.equals(today)) {
    return 'today';
  }

  if (day.equals(today.plusDays(1))) {
    return 'tomorrow';
  }

  return day.isBefore(today.plusDays(7)) ? 'next-seven-days' : 'later';
}

export const dueBucketLabels: Record<DueBucket, string> = {
  today: 'Today',
  tomorrow: 'Tomorrow',
  'next-seven-days': 'Next 7 days',
  later: 'Later',
  none: 'No due date'
};

/** "in 2 h" style text for the last push, so "already handed in" is visible at a glance. */
export function lastPushLabel(lastPushAt: Instant | null, now: Instant): string {
  if (lastPushAt === null) {
    return 'No commits yet';
  }

  return `Last push ${relativeDue(lastPushAt, now)}`;
}

/**
 * Whether students cannot see the assignment right now: it is unpublished and either has no publish time or that
 * time is still ahead. The backend applies the same rule, so nothing has to wait for the scheduled job.
 */
export function isHiddenNow(assignment: { isPublished: boolean; publishAt: Instant | null }, now: Instant): boolean {
  return !assignment.isPublished && (assignment.publishAt === null || assignment.publishAt.isAfter(now));
}
