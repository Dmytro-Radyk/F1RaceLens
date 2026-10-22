import { HttpClient } from '@angular/common/http';
import { DestroyRef, effect, inject, Service, signal } from '@angular/core';
import type { Subscription } from 'rxjs';
import { AuthService } from '../../auth/services/auth.service';
import { LocaleService } from '../../core/i18n/locale.service';

export interface ChatSource {
  id: number;
  title: string;
  url: string;
}
export interface ChatMessage {
  role: 'user' | 'assistant';
  content: string;
  sources?: ChatSource[];
  kind?: 'model' | 'reference';
  webStatus?: 'off' | 'ok' | 'empty' | 'unavailable';
  webSources?: ChatSource[];
}
interface ChatReply {
  answer: string;
  sources: ChatSource[];
  model: string;
  kind?: 'model' | 'reference';
  webStatus?: 'off' | 'ok' | 'empty' | 'unavailable';
  webSources?: ChatSource[];
}

@Service()
export class AssistantService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly locale = inject(LocaleService);
  readonly messages = signal<ChatMessage[]>([]);
  readonly draft = signal({ message: '' });
  readonly busy = signal(false);
  readonly error = signal('');
  readonly webSearch = signal(true);
  private request?: Subscription;
  private generation = 0;
  private identity = this.auth.user()?.id;
  private readonly channel =
    typeof BroadcastChannel === 'undefined' ? null : new BroadcastChannel('f1-chat-clear');

  constructor() {
    effect(() => {
      const identity = this.auth.user()?.id;
      if (identity !== this.identity) {
        this.identity = identity;
        this.clear();
        this.channel?.postMessage('clear');
      }
    });
    this.channel?.addEventListener('message', () => this.clear());
    // Also covers back-forward cache: returning to a closed/navigated-away document starts fresh.
    const clear = () => this.clear();
    window.addEventListener('pagehide', clear);
    inject(DestroyRef).onDestroy(() => {
      this.stop();
      this.channel?.close();
      window.removeEventListener('pagehide', clear);
    });
  }

  clear(): void {
    this.stop();
    this.messages.set([]);
    this.draft.set({ message: '' });
    this.error.set('');
  }

  stop(): void {
    this.generation++;
    this.request?.unsubscribe();
    this.request = undefined;
    this.busy.set(false);
  }

  send(text: string, page?: string): void {
    const content = text.trim();
    if (!content || content.length > 1500 || this.busy()) return;
    // A stopped/failed answer leaves a user message. Replace it for a valid alternating request.
    const previous =
      this.messages().at(-1)?.role === 'user' ? this.messages().slice(0, -1) : this.messages();
    const visible = [...previous, { role: 'user' as const, content }];
    this.messages.set(visible);
    let context = visible
      .slice(-11)
      .map((m) => ({ role: m.role, content: m.content.slice(0, 3000) }));
    while (context.length > 1 && context.reduce((sum, m) => sum + m.content.length, 0) > 5000)
      context = context.slice(2);
    const generation = ++this.generation;
    this.error.set('');
    this.busy.set(true);
    this.request = this.http
      .post<ChatReply>('/api/assistant/chat', {
        language: this.locale.language(),
        messages: context,
        page,
        webSearch: this.webSearch(),
      })
      .subscribe({
        next: (reply) => {
          if (generation !== this.generation) return;
          this.messages.update((messages) => [
            ...messages,
            {
              role: 'assistant',
              content: reply.answer,
              kind: reply.kind ?? 'model',
              webStatus: reply.webStatus ?? 'off',
              webSources: reply.webSources?.filter((s) => safeSource(s.url)) ?? [],
              sources: reply.sources.filter((s) => safeSource(s.url)),
            },
          ]);
          this.busy.set(false);
        },
        error: (error: { status?: number }) => {
          if (generation !== this.generation) return;
          this.busy.set(false);
          this.error.set(
            error.status === 429
              ? 'Асистент зайнятий. Спробуйте за хвилину.'
              : 'Асистент тимчасово недоступний. Спробуйте пізніше.',
          );
        },
      });
  }
}

export function safeSource(url: string): boolean {
  if (
    /^\/(?:drivers|teams|calendar|standings|predictions|history)(?:[/?#]|$)/.test(url) &&
    !url.startsWith('//')
  )
    return true;
  try {
    const parsed = new URL(url);
    return (
      parsed.protocol === 'https:' &&
      !parsed.username &&
      !parsed.password &&
      !parsed.port &&
      [
        'www.bbc.co.uk',
        'www.bbc.com',
        'www.formula1.com',
        'www.fia.com',
        'corp.formula1.com',
        'uk.wikipedia.org',
        'en.wikipedia.org',
        'de.wikipedia.org',
        'it.wikipedia.org',
        'es.wikipedia.org',
        'fr.wikipedia.org',
      ].includes(parsed.hostname)
    );
  } catch {
    return false;
  }
}
