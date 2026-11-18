import { Component, computed, effect, ElementRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { MatIcon } from '@angular/material/icon';
import { firstValueFrom } from 'rxjs';
import { AssignmentService } from '../../../core/services/assignment-service';
import { CourseService } from '../../../core/services/course-service';
import { SearchPaletteService } from '../../../core/services/search-palette-service';
import { SessionService } from '../../../core/auth/session-service';

/** One thing the palette can jump to. */
export interface SearchEntry {
  /** Material icon of the entry. */
  icon: string;
  title: string;
  subtitle: string;
  route: readonly (string | number)[];
}

/**
 * The search palette over every page. It knows the courses of the signed in user and the
 * assignments of them, and it opens with Ctrl K (Cmd K on macOS) from anywhere.
 */
@Component({
  selector: 'app-search-palette',
  imports: [MatIcon],
  templateUrl: './search-palette.html',
  styleUrl: './search-palette.scss'
})
export class SearchPalette {
  private readonly router = inject(Router);
  private readonly courseService = inject(CourseService);
  private readonly assignmentService = inject(AssignmentService);
  private readonly session = inject(SessionService);
  protected readonly palette = inject(SearchPaletteService);

  /** Everything the palette can find. */
  protected readonly entries = signal<SearchEntry[]>([]);
  protected readonly query = signal('');
  protected readonly active = signal(0);
  protected readonly loading = signal(false);

  private readonly field = viewChild<ElementRef<HTMLInputElement>>('field');
  private loaded = false;

  /** The entries that match what was typed, in the order they were found. */
  protected readonly results = computed<SearchEntry[]>(() => {
    const query = this.query().trim().toLocaleLowerCase();
    const entries = this.entries();
    if (query === '') {
      return entries;
    }

    return entries.filter(entry =>
      entry.title.toLocaleLowerCase().includes(query)
      || entry.subtitle.toLocaleLowerCase().includes(query));
  });

  constructor() {
    // The list is shorter after every keystroke, so the highlighted entry starts at the top.
    effect(() => {
      this.query();
      this.active.set(0);
    });

    // The palette is opened from the bar, from the courses page or with the keyboard.
    effect(() => {
      if (this.palette.opened()) {
        void this.load();
        setTimeout(() => this.field()?.nativeElement.focus());
      }
    });
  }

  /** Ctrl/Cmd + K opens the palette wherever the student is on the page. */
  @HostListener('document:keydown', ['$event'])
  protected onShortcut(event: KeyboardEvent): void {
    if (event.key.toLocaleLowerCase() !== 'k' || !(event.ctrlKey || event.metaKey)) {
      return;
    }

    event.preventDefault();
    this.palette.toggle();
  }

  protected close(): void {
    this.palette.close();
  }

  protected onInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const results = this.results();
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.active.set(this.active() + 1 >= results.length ? 0 : this.active() + 1);

      return;
    }

    if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.active.set(this.active() - 1 < 0 ? Math.max(results.length - 1, 0) : this.active() - 1);

      return;
    }

    if (event.key === 'Enter') {
      const entry = results[this.active()];
      if (entry) {
        event.preventDefault();
        this.choose(entry);
      }

      return;
    }

    if (event.key === 'Escape') {
      event.preventDefault();
      this.close();
    }
  }

  /** Jumps to the entry and closes the palette. */
  protected choose(entry: SearchEntry): void {
    this.close();
    void this.router.navigate(entry.route);
  }

  /** Reads the courses and the assignments of the signed in user, once. */
  private async load(): Promise<void> {
    if (this.loaded) {
      return;
    }

    this.loaded = true;
    this.loading.set(true);

    const courses = await this.courseService.getCourses();
    if (courses === null) {
      this.loading.set(false);

      return;
    }

    const entries: SearchEntry[] = courses.map(course => ({
      icon: 'school',
      title: course.title,
      subtitle: 'Course',
      route: ['/my-assignments/course', String(course.id)]
    }));

    if (this.session.isTeacher()) {
      for (const course of courses) {
        const assignments = await firstValueFrom(this.assignmentService.listForCourse$(course.id));
        for (const assignment of assignments) {
          entries.push({
            icon: 'assignment',
            title: assignment.title,
            subtitle: course.title,
            route: ['/assignments', String(assignment.id), 'students']
          });
        }
      }
    } else {
      const mine = await this.assignmentService.listMine();
      for (const assignment of mine?.accepted ?? []) {
        entries.push({
          icon: 'assignment',
          title: assignment.title,
          subtitle: assignment.courseTitle,
          route: ['/my-assignments', String(assignment.id)]
        });
      }
      for (const assignment of mine?.unaccepted ?? []) {
        entries.push({
          icon: 'assignment',
          title: assignment.title,
          subtitle: assignment.courseTitle,
          route: ['/my-assignments', String(assignment.id)]
        });
      }
    }

    this.entries.set(entries);
    this.loading.set(false);
  }
}
