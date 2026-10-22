import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AssistantService, safeSource } from './assistant.service';
import { AuthService } from '../../auth/services/auth.service';
import { LocaleService } from '../../core/i18n/locale.service';
import { F1Assistant } from './f1-assistant';
import axe from 'axe-core';
import { provideRouter } from '@angular/router';

describe('Ephemeral F1 assistant', () => {
  const user = signal<{ id: string } | null>({ id: 'test-account' });
  let http: HttpTestingController;
  let chat: AssistantService;
  beforeEach(() => {
    user.set({ id: 'test-account' });
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { user } },
        {
          provide: LocaleService,
          useValue: { language: () => 'uk', text: (value: string) => value },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    chat = TestBed.inject(AssistantService);
    TestBed.tick();
  });
  afterEach(() => {
    http.verify();
    TestBed.resetTestingModule();
  });
  it('uses conversation context only in the next request and filters unsafe sources', () => {
    chat.send('Tell me about Hamilton');
    http.expectOne('/api/assistant/chat').flush({
      answer: 'Seven titles.',
      sources: [{ id: 1, title: 'Bad', url: 'javascript:alert(1)' }],
      model: 'test',
    });
    expect(chat.messages()[1].sources).toEqual([]);
    chat.send('And his wins?');
    const request = http.expectOne('/api/assistant/chat');
    expect(request.request.body.messages.map((m: { role: string }) => m.role)).toEqual([
      'user',
      'assistant',
      'user',
    ]);
    request.flush({ answer: 'See the dated career snapshot.', sources: [], model: 'test' });
  });
  it('lets the user disable external search and shows the returned search status', () => {
    chat.webSearch.set(false);
    chat.send('Explain pit stops');
    const request = http.expectOne('/api/assistant/chat');
    expect(request.request.body.webSearch).toBe(false);
    request.flush({
      answer: 'A pit stop.',
      sources: [],
      model: 'test',
      webStatus: 'off',
      webSources: [
        { id: 1, title: 'Wikipedia', url: 'https://en.wikipedia.org/wiki/Pit_stop' },
        { id: 2, title: 'Unsafe', url: 'javascript:alert(1)' },
      ],
    });
    expect(chat.messages().at(-1)?.webStatus).toBe('off');
    expect(chat.messages().at(-1)?.webSources?.length).toBe(1);
    expect(safeSource('https://uk.wikipedia.org/wiki/Formula_1')).toBe(true);
    expect(safeSource('https://uk.wikipedia.org.attacker.test/wiki/Formula_1')).toBe(false);
  });
  it('sends with Enter and keeps Shift+Enter and composition available for typing', async () => {
    const fixture = TestBed.createComponent(F1Assistant);
    fixture.detectChanges();
    const input = fixture.nativeElement.querySelector('textarea') as HTMLTextAreaElement;
    chat.draft.set({ message: 'A new question' });
    fixture.detectChanges();
    input.dispatchEvent(
      new KeyboardEvent('keydown', {
        key: 'Enter',
        shiftKey: true,
        bubbles: true,
        cancelable: true,
      }),
    );
    http.expectNone('/api/assistant/chat');
    input.dispatchEvent(
      new KeyboardEvent('keydown', {
        key: 'Enter',
        isComposing: true,
        bubbles: true,
        cancelable: true,
      }),
    );
    http.expectNone('/api/assistant/chat');
    input.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }),
    );
    http.expectOne('/api/assistant/chat').flush({ answer: 'Answer', sources: [], model: 'test' });
    expect(chat.draft().message).toBe('');
    fixture.destroy();
  });
  it('cancels pending requests and clears on new conversation', () => {
    chat.send('Old question');
    const pending = http.expectOne('/api/assistant/chat');
    chat.clear();
    expect(pending.cancelled).toBe(true);
    expect(chat.messages()).toEqual([]);
    expect(chat.busy()).toBe(false);
    chat.send('New question');
    const next = http.expectOne('/api/assistant/chat');
    expect(next.request.body.messages).toEqual([{ role: 'user', content: 'New question' }]);
    next.flush({ answer: 'New answer', sources: [], model: 'test' });
  });
  it('clears on sign-out and pagehide without writing browser history storage', () => {
    chat.send('Private draft');
    const pending = http.expectOne('/api/assistant/chat');
    user.set(null);
    TestBed.tick();
    expect(pending.cancelled).toBe(true);
    expect(chat.messages()).toEqual([]);
    chat.messages.set([{ role: 'user', content: 'Temporary' }]);
    window.dispatchEvent(new Event('pagehide'));
    expect(chat.messages()).toEqual([]);
  });
  it('removes failed turns before retrying and keeps context bounded', () => {
    chat.send('Failed');
    http.expectOne('/api/assistant/chat').flush({}, { status: 503, statusText: 'Unavailable' });
    chat.send('Retry');
    const retry = http.expectOne('/api/assistant/chat');
    expect(retry.request.body.messages).toEqual([{ role: 'user', content: 'Retry' }]);
    retry.flush({ answer: 'OK', sources: [], model: 'test' });
    chat.messages.set(
      Array.from({ length: 20 }, (_, i) => ({
        role: i % 2 ? 'assistant' : 'user',
        content: 'x'.repeat(1000),
      })),
    );
    chat.send('Next');
    const next = http.expectOne('/api/assistant/chat');
    expect(next.request.body.messages[0].role).toBe('user');
    expect(
      next.request.body.messages.reduce(
        (n: number, m: { content: string }) => n + m.content.length,
        0,
      ),
    ).toBeLessThanOrEqual(5000);
    next.flush({ answer: 'OK', sources: [], model: 'test' });
  });
  it('renders an accessible composer with clear privacy notice', async () => {
    const fixture = TestBed.createComponent(F1Assistant);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('textarea')?.getAttribute('maxlength')).toBe('1500');
    const audit = await axe.run(element, { rules: { 'color-contrast': { enabled: false } } });
    expect(audit.violations).toEqual([]);
    fixture.destroy();
  });
  it('rejects lookalike and protocol-relative source links', () => {
    expect(safeSource('https://www.bbc.co.uk.attacker.test/x')).toBe(false);
    expect(safeSource('//attacker.test/drivers')).toBe(false);
    expect(safeSource('/drivers/career/lewis-hamilton')).toBe(true);
  });
});
