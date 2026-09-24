import { apiClient } from '../client';

/** The company's own details printed on every document (letterhead, footer, bank details, default texts). */
export type CompanyProfile = {
  name: string;
  managerName: string | null;
  address: string | null;
  city: string | null;
  phone: string | null;
  email: string | null;
  website: string | null;
  taxNumber: string | null;
  whatsApp: string | null;
  bankName: string | null;
  bankAccount: string | null;
  iban: string | null;
  invoiceSubtitle: string | null;
  /** One term per line. */
  invoiceTerms: string | null;
  receiptNote: string | null;
  statementNote: string | null;
  updatedAt?: string | null;
};

export const companyProfileApi = {
  get: () => apiClient.get('/company-profile'),
  update: (body: CompanyProfile) => apiClient.put('/company-profile', body),
};
