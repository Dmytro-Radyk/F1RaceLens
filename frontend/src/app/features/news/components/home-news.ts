import { Component, computed, inject, linkedSignal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../../core/i18n/translate.pipe';
import { currentLocale } from '../../../core/i18n/locale.service';
import { NewsService } from '../news.service';
import { NewsImage } from './news-image';
@Component({
  selector: 'app-home-news',
  imports: [TranslatePipe, RouterLink, NewsImage],
  templateUrl: './home-news.html',
  styleUrl: './home-news.css',
})
export class HomeNews {
  protected date(value: string): string {
    return new Intl.DateTimeFormat(currentLocale(), {
      dateStyle: 'medium',
      timeStyle: 'short',
    }).format(new Date(value));
  }
  protected readonly feed = inject(NewsService).feed;
  protected readonly stories = computed(() =>
    this.feed.hasValue() ? this.feed.value().articles.slice(0, 5) : [],
  );
  protected readonly index = linkedSignal({ source: this.stories, computation: () => 0 });
  protected readonly story = computed(() => this.stories()[this.index()]);
  private start: { x: number; y: number; pointer: number } | null = null;
  private swiped = false;
  protected move(delta: number): void {
    const count = this.stories().length;
    if (count) this.index.update((i) => (i + delta + count) % count);
  }
  protected down(event: PointerEvent): void {
    this.swiped = false;
    this.start = { x: event.clientX, y: event.clientY, pointer: event.pointerId };
  }
  protected cancel(): void {
    this.start = null;
  }
  protected up(event: PointerEvent): void {
    if (!this.start || event.pointerId !== this.start.pointer) return;
    const dx = event.clientX - this.start.x,
      dy = event.clientY - this.start.y;
    this.start = null;
    if (Math.abs(dx) > 50 && Math.abs(dx) > Math.abs(dy) * 1.5) {
      this.swiped = true;
      this.move(dx < 0 ? 1 : -1);
    }
  }
  protected follow(event: MouseEvent): void {
    if (this.swiped) {
      event.preventDefault();
      this.swiped = false;
    }
  }
}
