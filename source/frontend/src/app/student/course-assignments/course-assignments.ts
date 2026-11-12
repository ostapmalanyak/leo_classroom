import { Component, computed, inject, input, OnInit, signal, WritableSignal } from '@angular/core';
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

const SEARCH_DEBOUNCE_MS = 300;
const MIN_SEARCH_LENGTH = 2;

interface CourseAssignment {
  id: number;
  title: string;
  deadline: Instant | null;
  accepted: boolean | null;
  status: SubmissionStatus | null;
}

type DateSort = 'date-ascending' | 'date-descending';

interface AssignmentDateGroup {
  key: string;
  date: Date;
  assignments: CourseAssignment[];
}

interface AssignmentSections {
  open: AssignmentDateGroup[];
  noDate: CourseAssignment[];
  expired: AssignmentDateGroup[];
}

@Component({
  selector: 'app-course-assignments',
  imports: [
    DatePipe, NgTemplateOutlet, RouterLink, FormsModule, MatButton, MatIconButton, MatCard, MatCardContent,
    MatFormField, MatLabel, MatIcon, MatInput, MatMenuModule, MatProgressBar, MatTabsModule, CourseDetail
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

    // Every dated assignment is grouped by its deadline date, open and expired alike, so the
    // date is read from the subsection heading instead of from each card. Undated work gets
    // its own flat section.
    const byDate = new Map<string, AssignmentDateGroup>();
    const expiredByDate = new Map<string, AssignmentDateGroup>();
    const noDate: CourseAssignment[] = [];

    for (const assignment of this.assignments()) {
      if (assignment.deadline === null) {
        noDate.push(assignment);
        continue;
      }

      const date = this.formatDeadline(assignment.deadline);
      const groups = this.isExpired(date) ? expiredByDate : byDate;
      const key = this.dateKey(date);
      const group: AssignmentDateGroup = groups.get(key) ?? { key, date, assignments: [] };
      group.assignments.push(assignment);
      groups.set(key, group);
    }

    const toGroups = (groups: Map<string, AssignmentDateGroup>): AssignmentDateGroup[] =>
      [...groups.values()]
        .sort((left, right) => direction === 'date-descending'
          ? right.date.getTime() - left.date.getTime()
          : left.date.getTime() - right.date.getTime())
        .map(group => ({ ...group, assignments: group.assignments.sort(compare) }));

    return { open: toGroups(byDate), noDate: noDate.sort(compare), expired: toGroups(expiredByDate) };
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

  protected isExpired(date: Date): boolean {
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    return date.getTime() < today.getTime();
  }

  private dateKey(date: Date): string {
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  }

  public async ngOnInit(): Promise<void> {
    const tab = this.route.snapshot.queryParamMap.get('tab');
    if (tab === 'settings' && this.session.isTeacher()) {
      this.selectedTab.set(2);
    } else if (tab === 'participants') {
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
          status: null
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
          status: assignment.status
        }));
      this.assignments.set(assignments);
    } finally {
      this.loading.set(false);
    }
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
