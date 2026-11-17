import { Component, computed, inject, input, OnInit, signal, Signal, WritableSignal } from '@angular/core';
import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatButton, MatIconButton } from '@angular/material/button';
import { MatCard, MatCardContent } from '@angular/material/card';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatIcon } from '@angular/material/icon';
import { MatInput } from '@angular/material/input';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { Instant } from '@js-joda/core';
import { CourseDetail } from '../../course-detail/course-detail';
import { CourseIcon } from '../../shared/course-icon/course-icon';
import { AssignmentService, SubmissionStatus } from '../../../core/services/assignment-service';
import {
  CourseParticipant, CourseService, CourseTeacherParticipant
} from '../../../core/services/course-service';
import { CourseTeacherService } from '../../../core/services/course-teacher-service';
import { MeService } from '../../../core/services/me-service';
import { SessionService } from '../../../core/auth/session-service';
import { SnackbarService } from '../../../core/services/snackbar-service';
import { UserRole, UserService, UserSummary } from '../../../core/services/user-service';
import { Role } from '../../../core/auth/roles';
import {
  AssignmentView,
  assignmentViewOf,
  relativeDue,
  studentAssignmentState,
  timestampLabel
} from '../../../core/util/assignment-state';

const SEARCH_DEBOUNCE_MS = 300;
const MIN_SEARCH_LENGTH = 2;

interface CourseAssignment {
  id: number;
  title: string;
  deadline: Instant | null;
  accepted: boolean | null;
  status: SubmissionStatus | null;
  /** Last push of the student's own repository; only a student knows this. */
  lastPushAt: Instant | null;
  late: boolean;
}

/** A student assignment with the state it is in, the same states the My Assignments page uses. */
interface StudentRow {
  id: number;
  title: string;
  deadline: Instant | null;
  accepted: boolean;
  view: AssignmentView;
  /** Relative due text, shown inside the card: "Due in 3 days", "Overdue · 5 days ago". */
  due: string;
  /** Push timestamp for work that is already in the student's repository. */
  pushed: string;
}

type DateSort = 'date-ascending' | 'date-descending';

interface AssignmentDateGroup {
  key: string;
  date: Date;
  assignments: CourseAssignment[];
}

interface AssignmentSections {
  dated: AssignmentDateGroup[];
  noDate: CourseAssignment[];
}

@Component({
  selector: 'app-course-assignments',
  imports: [
    DatePipe, NgTemplateOutlet, RouterLink, FormsModule, MatButton, MatIconButton, MatCard,
    MatCardContent, MatFormField, MatLabel, MatIcon, MatInput, MatMenuModule, MatProgressBar, MatTabsModule,
    CourseDetail, CourseIcon
  ],
  templateUrl: './course-assignments.html',
  styleUrls: ['./course-assignments.scss', '../../my-courses/my-courses.scss']
})
export class CourseAssignments implements OnInit {
  private readonly assignmentService = inject(AssignmentService);
  private readonly route = inject(ActivatedRoute);
  private readonly courseService = inject(CourseService);
  private readonly courseTeacherService = inject(CourseTeacherService);
  private readonly userService = inject(UserService);
  private readonly me = inject(MeService);
  protected readonly session = inject(SessionService);
  private readonly snackbar = inject(SnackbarService);

  /** Bound from the route parameter (withComponentInputBinding). */
  public readonly courseId = input.required<string>();

  protected readonly title: WritableSignal<string> = signal('Course');
  protected readonly assignments: WritableSignal<CourseAssignment[]> = signal([]);
  protected readonly participants: WritableSignal<CourseParticipant[]> = signal([]);
  protected readonly teachers: WritableSignal<CourseTeacherParticipant[]> = signal([]);
  protected readonly loading: WritableSignal<boolean> = signal(false);
  protected readonly selectedTab = signal(0);
  protected readonly dateSort = signal<DateSort>('date-ascending');
  protected readonly sortByTitle = signal(false);

  /** The state switch a student sees: the same To do / Pushed / Past due as My Assignments. */
  protected readonly studentView = signal<AssignmentView>('todo');
  protected readonly accepting = signal<number | null>(null);

  /** The icon seed of every course, so a course always shows the icon it really has. */
  protected readonly iconSeeds: WritableSignal<Map<number, string | null>> = signal(new Map());

  /** Students see their work split by state, teachers see every assignment of the course. */
  protected readonly studentRows: Signal<StudentRow[]> = computed(() => {
    const now = Instant.now();

    return this.assignments().map(assignment => {
      const view = assignmentViewOf(studentAssignmentState({
        accepted: assignment.accepted === true,
        status: assignment.status,
        lastPushAt: assignment.lastPushAt,
        late: assignment.late,
        deadline: assignment.deadline
      }, now));

      return {
        id: assignment.id,
        title: assignment.title,
        deadline: assignment.deadline,
        accepted: assignment.accepted === true,
        view,
        due: this.dueText(assignment.deadline, now),
        pushed: view === 'pushed' && assignment.lastPushAt !== null
          ? `Pushed ${timestampLabel(assignment.lastPushAt)}`
          : ''
      };
    });
  });

  protected readonly todoCount: Signal<number> = computed(() => this.countStudentRows('todo'));
  protected readonly pushedCount: Signal<number> = computed(() => this.countStudentRows('pushed'));
  protected readonly pastDueCount: Signal<number> = computed(() => this.countStudentRows('past-due'));

  /** The rows of the state the student is looking at, soonest deadline first. */
  protected readonly visibleStudentRows: Signal<StudentRow[]> = computed(() =>
    this.studentRows()
      .filter(row => row.view === this.studentView())
      .sort((left, right) => {
        if (left.deadline === null && right.deadline === null) {
          return left.title.localeCompare(right.title);
        }
        if (left.deadline === null) {
          return 1;
        }
        if (right.deadline === null) {
          return -1;
        }

        return left.deadline.compareTo(right.deadline);
      }));

  /**
   * The heading follows the tab that is open. Students have an extra Overview tab in front of
   * Participants, so the index of the participants tab depends on the role.
   */
  protected readonly heading: Signal<string> = computed(() => {
    const index = this.selectedTab();

    if (index === 0) {
      return this.title();
    }

    if (this.session.isTeacher()) {
      return `${this.title()}'s ${index === 1 ? 'Participants' : 'Settings'}`;
    }

    // Overview is a shortcut to My Assignments, so the course keeps its own name there.
    return index === 2 ? `${this.title()}'s Participants` : this.title();
  });
  protected readonly assignmentSections = computed<AssignmentSections>(() => {
    const direction = this.dateSort();
    const titleFirst = this.sortByTitle();
    const compare = (left: CourseAssignment, right: CourseAssignment): number => {
      if (titleFirst) {
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

      return direction === 'date-descending' ? -byDate : byDate;
    };

    // Every dated assignment is grouped by its deadline date, so the date is read from the
    // subsection heading instead of from each card. Undated work gets its own flat section.
    const byDate = new Map<string, AssignmentDateGroup>();
    const noDate: CourseAssignment[] = [];

    for (const assignment of this.assignments()) {
      if (assignment.deadline === null) {
        noDate.push(assignment);
        continue;
      }

      const date = this.formatDeadline(assignment.deadline);
      const key = this.dateKey(date);
      const group: AssignmentDateGroup = byDate.get(key) ?? { key, date, assignments: [] };
      group.assignments.push(assignment);
      byDate.set(key, group);
    }

    const toGroups = (groups: Map<string, AssignmentDateGroup>): AssignmentDateGroup[] =>
      [...groups.values()]
        .sort((left, right) => direction === 'date-descending'
          ? right.date.getTime() - left.date.getTime()
          : left.date.getTime() - right.date.getTime())
        .map(group => ({ ...group, assignments: group.assignments.sort(compare) }));

    return { dated: toGroups(byDate), noDate: noDate.sort(compare) };
  });

  protected readonly students = computed(() => this.participants().filter(participant => participant.role === Role.Student));
  protected readonly showStudentSearch = signal(false);
  protected readonly studentSearchTerm = signal('');
  protected readonly studentSearchResults = signal<CourseParticipant[]>([]);
  protected readonly studentSearching = signal(false);
  protected readonly showTeacherSearch = signal(false);
  protected readonly teacherSearchTerm = signal('');
  protected readonly teacherSearchResults = signal<UserSummary[]>([]);
  protected readonly teacherSearching = signal(false);

  private studentSearchTimer: ReturnType<typeof setTimeout> | null = null;
  private teacherSearchTimer: ReturnType<typeof setTimeout> | null = null;

  protected get coursesRoute(): string {
    return this.session.isTeacher() ? '/my-courses' : '/my-assignments';
  }

  protected formatDeadline(deadline: Instant): Date {
    return new Date(deadline.toEpochMilli());
  }

  /** The seed of a course's icon, so a course always shows the icon it really has. */
  protected iconSeed(): string | null {
    return this.iconSeeds().get(this.courseIdNumber()) ?? null;
  }

  /** The course id as a number, so the icon component can look the seed up. */
  protected courseIdNumber(): number {
    return Number(this.courseId());
  }

  /** Switches between the To do / Pushed / Past due states a student works in. */
  protected setStudentView(view: AssignmentView): void {
    this.studentView.set(view);
  }

  /** Accepts an assignment from the course page, the same action the My Assignments page offers. */
  protected async accept(row: StudentRow): Promise<void> {
    this.accepting.set(row.id);
    const result = await this.assignmentService.accept(row.id);
    this.accepting.set(null);

    if (result === 'conflict') {
      await this.loadAssignments();

      return;
    }
    if (typeof result === 'string' || result === null) {
      this.snackbar.show('Could not accept the assignment');

      return;
    }

    this.assignments.update(assignments => assignments.map(assignment =>
      assignment.id === row.id
        ? { ...assignment, accepted: true, status: assignment.status ?? SubmissionStatus.Provisioning }
        : assignment));
    this.snackbar.show('Assignment accepted');
  }

  private countStudentRows(view: AssignmentView): number {
    return this.studentRows().filter(row => row.view === view).length;
  }

  /** The deadline is relative, so a card says "Due in 3 days" instead of a bare date. */
  private dueText(deadline: Instant | null, now: Instant): string {
    if (deadline === null) {
      return 'No due date';
    }

    const relative = relativeDue(deadline, now);

    return deadline.isBefore(now) ? `Overdue · ${relative}` : `Due ${relative}`;
  }

  private dateKey(date: Date): string {
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  }

  public async ngOnInit(): Promise<void> {
    const tab = this.route.snapshot.queryParamMap.get('tab');
    if (tab === 'settings' && this.session.isTeacher()) {
      this.selectedTab.set(2);
    } else if (tab === 'participants') {
      // Students have an Overview tab in front of Participants, so the index differs per role.
      this.selectedTab.set(this.session.isTeacher() ? 1 : 2);
    } else if (!this.session.isTeacher()) {
      // A student opens the course on Overview, where their own work is split by state.
      this.selectedTab.set(1);
    }

    this.loading.set(true);
    try {
      await this.me.load();

      const course = await this.courseService.getParticipants(Number(this.courseId()));
      if (course === null) {
        this.snackbar.show('Could not load course participants');

        return;
      }

      this.title.set(course.title);
      this.participants.set(course.participants);
      this.teachers.set(course.teachers);
      this.iconSeeds.set(await this.courseService.getCourseIconSeeds());

      await this.loadAssignments();
    } finally {
      this.loading.set(false);
    }
  }

  /** Loads the assignments of this course: all of them for a teacher, the student's own for a student. */
  private async loadAssignments(): Promise<void> {
    if (this.session.isTeacher()) {
      const assignments = await this.assignmentService.listForCourse(Number(this.courseId()));
      if (assignments === null) {
        this.snackbar.show('Could not load assignments');

        return;
      }
      this.assignments.set(assignments.map(assignment => ({
        id: assignment.id,
        title: assignment.title,
        deadline: assignment.deadline,
        accepted: null,
        status: null,
        lastPushAt: null,
        late: false
      })));

      return;
    }

    const mine = await this.assignmentService.listMine();
    if (mine === null) {
      this.snackbar.show('Could not load your assignments');

      return;
    }
    const courseId = Number(this.courseId());
    const assignments = [...mine.accepted, ...mine.unaccepted]
      .filter(assignment => assignment.courseId === courseId)
      .map(assignment => ({
        id: assignment.id,
        title: assignment.title,
        deadline: assignment.deadline,
        accepted: assignment.accepted,
        status: assignment.status,
        lastPushAt: assignment.lastPushAt,
        late: assignment.late
      }));
    this.assignments.set(assignments);
  }

  protected async removeParticipant(participant: CourseParticipant): Promise<void> {
    const removed = await this.courseService.removeParticipant(Number(this.courseId()), participant.id);
    if (!removed) {
      this.snackbar.show('Could not remove course participant');

      return;
    }

    this.participants.update(participants => participants.filter(member => member.id !== participant.id));
    this.snackbar.show('Participant removed from this course');
  }

  protected toggleStudentSearch(): void {
    this.showStudentSearch.update(show => !show);
    this.studentSearchTerm.set('');
    this.studentSearchResults.set([]);
  }

  protected handleStudentSearchChange(term: string): void {
    this.studentSearchTerm.set(term);
    if (this.studentSearchTimer !== null) {
      clearTimeout(this.studentSearchTimer);
    }
    if (term.trim().length < MIN_SEARCH_LENGTH) {
      this.studentSearchResults.set([]);
      return;
    }
    this.studentSearchTimer = setTimeout(() => void this.runStudentSearch(term.trim()), SEARCH_DEBOUNCE_MS);
  }

  protected async addStudent(user: CourseParticipant): Promise<void> {
    const added = await this.courseService.addParticipant(Number(this.courseId()), user.id);
    if (!added) {
      this.snackbar.show('Could not add student to this course');
      return;
    }

    this.participants.update(participants => participants.some(participant => participant.id === user.id)
      ? participants
      : [...participants, { id: user.id, firstName: user.firstName, lastName: user.lastName,
        class: user.class, role: Role.Student }]);
    this.toggleStudentSearch();
    this.snackbar.show(`Added ${user.firstName} ${user.lastName}`);
  }

  protected toggleTeacherSearch(): void {
    this.showTeacherSearch.update(show => !show);
    this.teacherSearchTerm.set('');
    this.teacherSearchResults.set([]);
  }

  protected handleTeacherSearchChange(term: string): void {
    this.teacherSearchTerm.set(term);
    if (this.teacherSearchTimer !== null) {
      clearTimeout(this.teacherSearchTimer);
    }
    if (term.trim().length < MIN_SEARCH_LENGTH) {
      this.teacherSearchResults.set([]);
      return;
    }
    this.teacherSearchTimer = setTimeout(() => void this.runTeacherSearch(term.trim()), SEARCH_DEBOUNCE_MS);
  }

  protected async addTeacher(user: UserSummary): Promise<void> {
    const result = await this.courseTeacherService.addTeacher(Number(this.courseId()), user.id);
    if (result !== 'ok') {
      this.snackbar.show(result === 'forbidden'
        ? 'Only the course owner or an admin can add co-teachers'
        : 'Could not add co-teacher');
      return;
    }

    this.teachers.update(teachers => teachers.some(teacher => teacher.id === user.id)
      ? teachers
      : [...teachers, { id: user.id, firstName: user.firstName, lastName: user.lastName, isOwner: false }]);
    this.toggleTeacherSearch();
  }

  protected async removeTeacher(teacher: CourseTeacherParticipant): Promise<void> {
    const result = await this.courseTeacherService.removeTeacher(Number(this.courseId()), teacher.id);
    if (result !== 'ok') {
      this.snackbar.show(result === 'forbidden'
        ? 'Only the course owner or an admin can remove co-teachers'
        : 'Could not remove co-teacher');
      return;
    }

    this.teachers.update(teachers => teachers.filter(member => member.id !== teacher.id));
  }

  protected teacherDisplayName(teacher: UserSummary): string {
    return `${teacher.firstName} ${teacher.lastName}`.trim().replace(/\s+/g, ' ');
  }

  private async runStudentSearch(term: string): Promise<void> {
    this.studentSearching.set(true);
    const results = await this.courseService.searchAvailableStudents(Number(this.courseId()), term);
    this.studentSearching.set(false);
    if (results === null) {
      this.snackbar.show('Could not search students');
      return;
    }

    this.studentSearchResults.set(results);
  }

  private async runTeacherSearch(term: string): Promise<void> {
    this.teacherSearching.set(true);
    const results = await this.userService.search(term, UserRole.Teacher);
    this.teacherSearching.set(false);
    if (results === null) {
      this.snackbar.show('Could not search teachers');
      return;
    }

    const existingIds = new Set(this.teachers().map(teacher => teacher.id));
    this.teacherSearchResults.set(results.filter(user =>
      !existingIds.has(user.id) && this.teacherDisplayName(user).length > 0));
  }

}
