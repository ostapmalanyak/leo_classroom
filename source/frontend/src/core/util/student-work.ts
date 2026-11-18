import { Instant } from '@js-joda/core';
import { AssignedAssignment } from '../services/assignment-service';
import {
  assignmentViewOf,
  AssignmentView,
  StudentAssignmentState,
  studentAssignmentState
} from './assignment-state';

/** One assignment, classified once. Every count and every list in the app comes from this shape. */
export interface WorkEntry {
  assignment: AssignedAssignment;
  state: StudentAssignmentState;
  view: AssignmentView;
}

/**
 * The single source of truth for "where does this assignment belong". If a tab, a course tile
 * and the home page disagree, it is because one of them computed its own answer; this is the
 * function that keeps them identical.
 */
export function workEntries(assignments: readonly AssignedAssignment[], now: Instant): WorkEntry[] {
  return assignments.map(assignment => {
    const state = studentAssignmentState(assignment, now);

    return { assignment, state, view: assignmentViewOf(state) };
  });
}

export interface ViewCounts {
  todo: number;
  pushed: number;
  pastDue: number;
}

export function countViews(entries: readonly WorkEntry[]): ViewCounts {
  return {
    todo: entries.filter(entry => entry.view === 'todo').length,
    pushed: entries.filter(entry => entry.view === 'pushed').length,
    pastDue: entries.filter(entry => entry.view === 'past-due').length
  };
}

export interface CourseWork extends ViewCounts {
  /** The earliest deadline among the assignments the student still has to hand in. */
  next: AssignedAssignment | null;
}

/**
 * What a course card says. "Open" means the same thing it means on the To do tab, and `next`
 * never points at work that is already pushed - that was the contradiction between the course
 * grid and the Submitted tab.
 */
export function summariseCourse(entries: readonly WorkEntry[]): CourseWork {
  const counts = countViews(entries);

  const next = entries
    .filter(entry => entry.view === 'todo' && entry.assignment.deadline !== null)
    .map(entry => entry.assignment)
    .sort((left, right) => left.deadline!.compareTo(right.deadline!))[0] ?? null;

  return { ...counts, next };
}
