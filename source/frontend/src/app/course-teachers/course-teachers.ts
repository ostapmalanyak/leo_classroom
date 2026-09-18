import { Component, inject, input, InputSignal, OnInit, signal, WritableSignal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatCard, MatCardContent, MatCardTitle } from '@angular/material/card';
import { MatFormField, MatLabel } from '@angular/material/form-field';
import { MatInput } from '@angular/material/input';
import { MatIconButton } from '@angular/material/button';
import { MatIcon } from '@angular/material/icon';
import { MatProgressBar } from '@angular/material/progress-bar';
import { MatListModule } from '@angular/material/list';
import { CourseTeachers, CourseTeacherService } from '../../core/services/course-teacher-service';
import { UserRole, UserService, UserSummary } from '../../core/services/user-service';
import { SnackbarService } from '../../core/services/snackbar-service';
import { SessionService } from '../../core/auth/session-service';

const SEARCH_DEBOUNCE_MS = 300;
const MIN_SEARCH_LENGTH = 2;

@Component({
  selector: 'app-course-teachers',
  imports: [
    FormsModule,
    MatCard,
    MatCardContent,
    MatCardTitle,
    MatFormField,
    MatLabel,
    MatInput,
    MatIconButton,
    MatIcon,
    MatProgressBar,
    MatListModule
  ],
  templateUrl: './course-teachers.html',
  styleUrl: './course-teachers.scss'
})
export class CourseTeacherManagement implements OnInit {
  private readonly teacherService = inject(CourseTeacherService);
  private readonly userService = inject(UserService);
  private readonly snackbar = inject(SnackbarService);
  protected readonly session = inject(SessionService);

  public readonly courseId: InputSignal<string> = input.required<string>({ alias: 'id' });

  protected readonly teachers: WritableSignal<CourseTeachers | null> = signal(null);
  protected readonly loading: WritableSignal<boolean> = signal(false);

  protected readonly searchTerm: WritableSignal<string> = signal('');
  protected readonly searchResults: WritableSignal<UserSummary[]> = signal([]);
  protected readonly searching: WritableSignal<boolean> = signal(false);

  private searchTimer: ReturnType<typeof setTimeout> | null = null;

  public async ngOnInit(): Promise<void> {
    await this.reload();
  }

  protected handleSearchChange(term: string): void {
    this.searchTerm.set(term);
    if (this.searchTimer !== null) {
      clearTimeout(this.searchTimer);
    }
    if (term.trim().length < MIN_SEARCH_LENGTH) {
      this.searchResults.set([]);

      return;
    }
    this.searchTimer = setTimeout(() => void this.runSearch(term.trim()), SEARCH_DEBOUNCE_MS);
  }

  protected async handleAddTeacher(user: UserSummary): Promise<void> {
    const result = await this.teacherService.addTeacher(Number(this.courseId()), user.id);
    if (result === 'forbidden') {
      this.snackbar.show('Only the owner or an admin can add co-teachers');

      return;
    }
    if (result === 'not-found') {
      this.snackbar.show('User must be an active teacher');

      return;
    }
    if (result !== 'ok') {
      this.snackbar.show('Could not add the co-teacher');

      return;
    }
    this.searchTerm.set('');
    this.searchResults.set([]);
    await this.reload();
  }

  protected async handleRemoveTeacher(user: UserSummary): Promise<void> {
    const result = await this.teacherService.removeTeacher(Number(this.courseId()), user.id);
    if (result === 'forbidden') {
      this.snackbar.show('The owner cannot be removed');

      return;
    }
    if (result !== 'ok') {
      this.snackbar.show('Could not remove the co-teacher');

      return;
    }
    await this.reload();
  }

  private async reload(): Promise<void> {
    this.loading.set(true);
    const result = await this.teacherService.getTeachers(Number(this.courseId()));
    this.loading.set(false);
    if (result === null) {
      this.snackbar.show('Could not load course teachers');

      return;
    }
    this.teachers.set(result);
  }

  private async runSearch(term: string): Promise<void> {
    this.searching.set(true);
    const results = await this.userService.search(term, UserRole.Teacher);
    this.searching.set(false);
    if (results === null) {
      this.snackbar.show('Could not search teachers');

      return;
    }
    this.searchResults.set(results);
  }
}
