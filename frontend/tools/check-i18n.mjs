import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import { parseTemplate } from '@angular/compiler';

const root = path.resolve(import.meta.dirname, '..');
const app = path.join(root, 'src/app');
const languages = ['en', 'de', 'it', 'es', 'fr'];
const errors = [];
const cyrillic = /[А-Яа-яІіЇїЄєҐґ]/;
const normalize = (value) => value.replace(/\s+/g, ' ').trim();
const placeholders = (value) =>
  [...value.matchAll(/\{\d+\}/g)]
    .map((match) => match[0])
    .sort()
    .join(',');
const parseTs = (file, source) => ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true);
function readCatalog(language) {
  const file = path.join(app, 'core/i18n/catalogs', language + '.ts');
  const source = fs.readFileSync(file, 'utf8');
  const catalog = {};
  function visit(node) {
    if (
      ts.isPropertyAssignment(node) &&
      (ts.isStringLiteral(node.name) || ts.isIdentifier(node.name)) &&
      ts.isStringLiteral(node.initializer)
    ) {
      if (Object.hasOwn(catalog, node.name.text))
        errors.push('Duplicate key: ' + language + ': ' + node.name.text);
      catalog[node.name.text] = node.initializer.text;
    }
    ts.forEachChild(node, visit);
  }
  visit(parseTs(file, source));
  return catalog;
}
const catalogs = Object.fromEntries(languages.map((language) => [language, readCatalog(language)]));
const reference = catalogs.en;
for (const [language, catalog] of Object.entries(catalogs)) {
  for (const key of new Set([...Object.keys(reference), ...Object.keys(catalog)])) {
    if (!catalog[key]?.trim() || !reference[key])
      errors.push('Missing key: ' + language + ': ' + key);
    else if (placeholders(catalog[key]) !== placeholders(key))
      errors.push('Placeholder mismatch: ' + language + ': ' + key);
    else if (cyrillic.test(catalog[key]))
      errors.push('Untranslated catalog value: ' + language + ': ' + key);
  }
}
function message(value, file) {
  const key = normalize(value);
  if (key === 'Українська') return; // Language names are autonyms in every locale.
  if (cyrillic.test(key) && !reference[key])
    errors.push(path.relative(root, file) + ': missing message: ' + key);
}
function template(source, file) {
  const result = parseTemplate(source, file);
  for (const error of result.errors ?? []) errors.push(String(error));
  const visited = new WeakSet();
  function visit(node) {
    if (!node || typeof node !== 'object' || visited.has(node)) return;
    visited.add(node);
    if (node.constructor.name === 'LiteralPrimitive' && typeof node.value === 'string')
      message(node.value, file);
    if (
      node.constructor.name === 'Text' &&
      /\p{L}/u.test(node.value) &&
      !/^(?:F1|F1 Race Lens|F1 RACE LENS|Q[123]|Open-Meteo|CC BY 4\.0|admin|Google|GitHub|MAE|Brier)\s*$/.test(
        normalize(node.value),
      )
    )
      errors.push(file + ': untranslated text: ' + node.value);
    if (node.constructor.name === 'TextAttribute' && cyrillic.test(node.value))
      message(node.value, file);
    for (const child of Object.values(node)) {
      if (Array.isArray(child)) child.forEach(visit);
      else visit(child);
    }
  }
  visit(result.nodes);
}
function walk(directory) {
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const file = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (file !== path.join(app, 'core/i18n/catalogs')) walk(file);
      continue;
    }
    if (file.endsWith('.spec.ts')) continue;
    const source = fs.readFileSync(file, 'utf8');
    if (file.endsWith('.html')) template(source, file);
    if (!file.endsWith('.ts')) continue;
    function visit(node) {
      if (
        ts.isPropertyAssignment(node) &&
        node.name.getText() === 'template' &&
        ts.isNoSubstitutionTemplateLiteral(node.initializer)
      ) {
        template(node.initializer.text, file);
        return;
      }
      if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node))
        message(node.text, file);
      if (ts.isTemplateExpression(node))
        message(
          node.head.text +
            node.templateSpans.map((span, i) => `{${i}}` + span.literal.text).join(''),
          file,
        );
      ts.forEachChild(node, visit);
    }
    visit(parseTs(file, source));
  }
}
walk(app);
if (errors.length) {
  console.error([...new Set(errors)].join('\n'));
  process.exitCode = 1;
} else
  console.log(
    `i18n OK: ${Object.keys(reference).length} source messages, five complete translated catalogs, no untranslated template text.`,
  );
