import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import ar from './ar.json';
import en from './en.json';

void i18n.use(initReactI18next).init({
  lng: 'ar',
  fallbackLng: 'en',
  resources: {
    ar: { translation: ar },
    en: { translation: en },
  },
  interpolation: {
    escapeValue: false,
  },
});

export { i18n };
