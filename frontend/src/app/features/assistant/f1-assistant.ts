import { afterRenderEffect, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { form, FormField, maxLength } from '@angular/forms/signals';
import { TranslatePipe } from '../../core/i18n/translate.pipe';
import { AssistantService } from './assistant.service';
import { ActivatedRoute } from '@angular/router';

@Component({
  selector: 'app-f1-assistant',
  imports: [TranslatePipe, FormField],
  templateUrl: './f1-assistant.html',
  styleUrl: './f1-assistant.css',
})
export class F1Assistant {
  protected readonly chat = inject(AssistantService);
  private readonly requestedPage = inject(ActivatedRoute).snapshot.queryParamMap.get('page');
  protected readonly page =
    this.requestedPage &&
    /^\/(home|news|calendar|standings|drivers|teams|compare|insights|predictions|history)([/a-zA-Z0-9_?=&-]*)$/.test(
      this.requestedPage,
    )
      ? this.requestedPage
      : undefined;
  private readonly draft = this.chat.draft;
  protected readonly fields = form(this.draft, (path) => maxLength(path.message, 1500));
  private readonly feed = viewChild<ElementRef<HTMLElement>>('feed');
  private readonly input = viewChild<ElementRef<HTMLTextAreaElement>>('input');
  protected readonly awayFromBottom = signal(false);
  constructor() {
    afterRenderEffect(() => {
      this.chat.messages();
      this.chat.busy();
      this.chat.error();
      if (!this.awayFromBottom()) this.scrollToBottom();
    });
  }
  protected scrollToBottom(): void {
    const feed = this.feed()?.nativeElement;
    if (feed) feed.scrollTop = feed.scrollHeight;
    this.awayFromBottom.set(false);
  }
  protected onScroll(): void {
    const feed = this.feed()?.nativeElement;
    if (feed) this.awayFromBottom.set(feed.scrollHeight - feed.scrollTop - feed.clientHeight > 100);
  }
  protected keydown(event: KeyboardEvent): void {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) this.send(event);
  }
  protected setWebSearch(event: Event): void {
    this.chat.webSearch.set((event.target as HTMLInputElement).checked);
  }
  protected webLabel(status?: string): string {
    return status === 'ok'
      ? 'Пошук у мережі: джерела знайдено'
      : status === 'empty'
        ? 'Пошук у мережі: відповідних джерел немає'
        : status === 'unavailable'
          ? 'Пошук у мережі тимчасово недоступний'
          : 'Без пошуку в мережі';
  }
  protected clear(): void {
    this.chat.clear();
    this.draft.set({ message: '' });
    this.awayFromBottom.set(false);
    this.input()?.nativeElement.focus();
  }
  protected retry(): void {
    const last = this.chat.messages().at(-1);
    if (last?.role === 'user') this.chat.send(last.content, this.page);
  }
  protected send(event: Event): void {
    event.preventDefault();
    if (this.fields().invalid() || this.chat.busy() || !this.draft().message.trim()) return;
    this.chat.send(this.draft().message, this.page);
    this.draft.set({ message: '' });
    this.awayFromBottom.set(false);
    this.input()?.nativeElement.focus();
  }
}
