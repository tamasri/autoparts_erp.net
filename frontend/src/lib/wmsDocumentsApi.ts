import { transfersApi } from '../api/endpoints/transfers';
import { issueOrdersApi } from '../api/endpoints/issueOrders';
import { cycleCountsApi } from '../api/endpoints/cycleCounts';
import { stockAdjustmentsApi } from '../api/endpoints/stockAdjustments';
import { receivingApi } from '../api/endpoints/receiving';

export const api = {
  transferOrder: (id: string) => transfersApi.getOrder(id),
  issueOrder: (id: string) => issueOrdersApi.get(id),
  cycleCount: (id: string) => cycleCountsApi.get(id),
  adjustment: (id: string) => stockAdjustmentsApi.get(id),
  receiving: (id: string) => receivingApi.get(id),
};
